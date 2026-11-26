using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommNet;
using HarmonyLib;

namespace LightLag
{
    /// <summary>
    /// Estimates when the vessel's signal next changes: when a vessel without a signal gets one
    /// (contact), or when a connected one loses it (loss). CommNet only knows who can reach
    /// whom right now, so LightLag steps forward in time and redoes the link checks itself.
    ///
    /// <para><b>Off by default:</b> the "Signal estimates" setting turns it on.</para>
    ///
    /// <para><b>Model:</b> stock CommNet's rules, applied to where everything will be. Every
    /// node keeps the antenna powers CommNet gave it now. Vessels in flight follow their current
    /// orbit (they coast), landed vessels and ground stations turn with their body (a station's
    /// place is read from its <see cref="CommNetHome"/>). A link needs
    /// <see cref="CommNetScenario.RangeModel"/> to put the pair in range and no body in the way;
    /// a body blocks a radius of its radius × <c>scaledRadiusHorizonMultiplier</c> × CommNet's
    /// occlusion multiplier (vacuum or atmosphere), like stock's horizon culling (as a sphere).
    /// A path can pass through nodes with relay antennas and must end at a control source:
    /// a ground station or a probe control point, as stock's control path does.</para>
    ///
    /// <para><b>Not modelled:</b> burns and staging, plasma blackout, power running out, and
    /// vessels changing sphere of influence (the search stops at the end of the vessel's orbit
    /// patch). If the model disagrees with CommNet at its last rebuild (it sees a path during
    /// plasma blackout, say), something outside it decides the signal, so there's no estimate.</para>
    ///
    /// <para><b>Stock CommNet only.</b> In RealAntennas mode (RealAntennas loaded) it's off,
    /// and so on any other network that replaces stock's: their links follow other rules.
    /// RealAntennas gets its own model with the adapter (docs/DESIGN.md, Q9).</para>
    ///
    /// <para><b>Search:</b> steps forward up to a horizon (two of the vessel's orbits or its
    /// body's days, from an hour up to 50 days), then bisects the first change to the second.
    /// Each step is short enough that no link can change within it: for every pair of nodes the
    /// path could use, it takes how far the pair is from changing (range left, or clearance over
    /// each body) over how fast that can change (their relative speed), and steps half the
    /// smallest such time. So a short gap or window between two steps isn't missed. Steps are
    /// at most 1/600 of the horizon, and at least a second (or 1/20000 of the horizon). It runs
    /// for about a millisecond per frame so it doesn't stall the game, and starts over every few
    /// seconds of real time, keeping the last answer until the new one is ready.</para>
    ///
    /// <para><b>From CommNet's last rebuild:</b> CommNet only rechecks its links every few
    /// seconds (every half-second of real time in time warp, so minutes of game time at high
    /// warp). The search starts at that rebuild, where the model must agree with CommNet's
    /// state, rather than at now. A change the model finds between the rebuild and now is one
    /// CommNet hasn't noticed yet: the estimate shows it as due now.</para>
    /// </summary>
    internal sealed class ContactPredictor
    {
        internal enum Status
        {
            /// <summary>Not searched yet, or the model disagrees with CommNet.</summary>
            Unknown,
            /// <summary>Off: the setting is off, RealAntennas mode, or another CommNet the model doesn't cover. Nothing is shown.</summary>
            Off,
            Found,
            NotFound,
        }

        private const int MaxStepFraction = 600, MinStepFraction = 20000, Bisections = 14;
        private const float RestartSeconds = 5f;
        private const double MinHorizon = 3600.0, MaxHorizonDays = 50.0;
        /// <summary>Of the time no link can change in, how much a step takes (margin for speeds changing).</summary>
        private const double StepSafety = 0.5;
        /// <summary>Seconds ahead for the finite-difference velocities.</summary>
        private const double VelocityDt = 1.0;
        private const double FrameBudgetMs = 1.0;

        public Status Result { get; private set; } = Status.Unknown;
        /// <summary>True when looking for contact (no signal now), false when looking for loss.</summary>
        public bool Contact => !_connected;
        /// <summary>When the signal is expected to change (Found).</summary>
        public double ChangeUT { get; private set; }
        /// <summary>The control source reached first, for a contact (Found).</summary>
        public string Via { get; private set; }
        /// <summary>How far ahead the search looked (NotFound).</summary>
        public double Horizon { get; private set; }
        /// <summary>A search is under way; <see cref="Result"/> is still the previous one.</summary>
        public bool Searching => _searching;

        private sealed class Node
        {
            public CommNode Comm;
            public string Name;
            public Orbit Orbit;
            public CelestialBody Body;
            public double Lat, Lon, Alt;
            public bool Relay, Target, MultiHop;
            public Vector3d Position, Velocity;
        }

        private sealed class Occluder
        {
            public CelestialBody Body;
            public double Radius;
            public Vector3d Position, Velocity;
        }

        /// <summary>A pair of nodes the path could link, with its range (powers don't change).</summary>
        private struct Pair
        {
            public int A, B;
            public double Range;
        }

        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Occluder> _occluders = new List<Occluder>();
        private readonly Dictionary<CelestialBody, Vector3d> _bodyAt = new Dictionary<CelestialBody, Vector3d>();
        private readonly List<Pair> _pairs = new List<Pair>();
        private readonly Queue<int> _queue = new Queue<int>();
        private readonly Stopwatch _watch = new Stopwatch();
        private bool[] _visited = new bool[0];
        private Vector3d[] _ahead = new Vector3d[0];
        private Node _start;
        private Guid _vesselId;
        private double _now, _end, _step, _minStep, _maxStep, _t;
        private bool _searching, _connected;
        private float _restartAt;
        private string _reachedVia;
        private static bool _loggedOff;
        private static bool? _realAntennas;

        // When CommNet last rebuilt its links, from the network's post-update callback.
        private static CommNetwork _watched;
        private static double _rebuildUT = double.NaN;

        private static readonly AccessTools.FieldRef<CommNetHome, CommNode> HomeComm = AccessTools.FieldRefAccess<CommNetHome, CommNode>("comm");
        private static readonly AccessTools.FieldRef<CommNetHome, CelestialBody> HomeBody = AccessTools.FieldRefAccess<CommNetHome, CelestialBody>("body");
        private static readonly AccessTools.FieldRef<CommNetHome, double> HomeLat = AccessTools.FieldRefAccess<CommNetHome, double>("lat");
        private static readonly AccessTools.FieldRef<CommNetHome, double> HomeLon = AccessTools.FieldRefAccess<CommNetHome, double>("lon");
        private static readonly AccessTools.FieldRef<CommNetHome, double> HomeAlt = AccessTools.FieldRefAccess<CommNetHome, double>("alt");

        /// <summary>RealAntennas is loaded, so it runs CommNet (whether or not its network is up yet).</summary>
        internal static bool RealAntennasMode =>
            _realAntennas ?? (_realAntennas = AssemblyLoader.loadedAssemblies.Any(a => a.name == "RealAntennas")).Value;

        /// <summary>The model covers stock's own network only, and never in RealAntennas mode.</summary>
        private static bool Covers(CommNetwork network) => !RealAntennasMode && network.GetType() == typeof(CommNetwork);
        private Guid _loggedUnknown;

        /// <summary>Clears the answer, as when delay stops applying.</summary>
        public void Reset()
        {
            Result = Status.Unknown;
            _searching = false;
            _vesselId = Guid.Empty;
            _restartAt = 0f;
        }

        /// <summary>
        /// Every frame while delay applies to the vessel: advances the search a little. With
        /// <paramref name="connected"/> it looks for loss, without it for contact.
        /// </summary>
        public void Update(Vessel vessel, bool connected)
        {
            Watch(CommNetNetwork.Instance?.CommNet);
            if (!(LightLagParams.Current?.signalEstimates ?? false))
            {
                Reset();
                Result = Status.Off;
                return;
            }
            if (vessel == null) { Reset(); return; }
            if (vessel.id != _vesselId || connected != _connected) { Reset(); _vesselId = vessel.id; _connected = connected; }
            if (!_searching)
            {
                if (UnityEngine.Time.unscaledTime < _restartAt) return;
                _restartAt = UnityEngine.Time.unscaledTime + RestartSeconds;
                Begin(vessel);
                if (!_searching) return;
            }

            _watch.Restart();
            while (_searching && _watch.Elapsed.TotalMilliseconds < FrameBudgetMs)
            {
                double previous = _t;
                _t = Math.Min(_t + _step, _end);
                Move(_t);
                if (Reached() != _connected)
                {
                    Finish(Bisect(previous, _t));
                    return;
                }
                if (_t >= _end)
                {
                    _searching = false;
                    if (Result != Status.NotFound) Log.Info($"{What}: none in the next {DelayService.Format(_end - _now)}");
                    Result = Status.NotFound;
                    Horizon = _end - _now;
                    return;
                }
                _step = Step();
            }
        }

        /// <summary>Keeps <see cref="_rebuildUT"/> following <paramref name="network"/>'s rebuilds.</summary>
        private static void Watch(CommNetwork network)
        {
            if (network == null || network == _watched) return;
            network.OnNetworkPostUpdate += () => _rebuildUT = Planetarium.GetUniversalTime();
            _watched = network;
            _rebuildUT = double.NaN;
        }

        private void Begin(Vessel vessel)
        {
            CommNetwork network = CommNetNetwork.Instance?.CommNet;
            if (network == null || vessel.connection?.Comm == null) { Result = Status.Unknown; return; }
            if (!Covers(network))
            {
                if (!_loggedOff)
                    Log.Info(RealAntennasMode ? "signal estimate: off in RealAntennas mode"
                                              : $"signal estimate: off on {network.GetType().FullName}");
                _loggedOff = true;
                Result = Status.Off;
                return;
            }

            _now = Planetarium.GetUniversalTime();
            Snapshot(network, vessel);
            if (_start == null) { Result = Status.Unknown; return; }

            // CommNet's state is from its last rebuild: start there. If the model disagrees with
            // CommNet then, something outside it decides the signal.
            double from = network == _watched && _rebuildUT <= _now ? _rebuildUT : _now;
            Move(from);
            if (Reached() != _connected)
            {
                if (Result != Status.Unknown || _vesselId != _loggedUnknown)
                    Log.Info(_connected ? $"{What}: the model sees no path, though CommNet does; no estimate"
                                        : $"{What}: the model sees a path to {_reachedVia}, so something else cuts the signal; no estimate");
                _loggedUnknown = _vesselId;
                Result = Status.Unknown;
                return;
            }

            double horizon = Math.Max(MinHorizon, 2.0 * Period(vessel));
            horizon = Math.Min(horizon, MaxHorizonDays * KSPUtil.dateTimeFormatter.Day);
            _end = _now + horizon;
            Orbit orbit = _start.Orbit;
            if (orbit != null && orbit.patchEndTransition != Orbit.PatchTransitionType.FINAL
                && orbit.patchEndTransition != Orbit.PatchTransitionType.INITIAL && orbit.EndUT > _now)
                _end = Math.Min(_end, orbit.EndUT);
            _maxStep = (_end - _now) / MaxStepFraction;
            _minStep = Math.Max(1.0, (_end - _now) / MinStepFraction);
            _t = from;
            _searching = _maxStep > 0;
            if (!_searching) { Result = Status.NotFound; return; }
            _step = Step();
        }

        /// <summary>One orbit, or one day of the body it sits on.</summary>
        private static double Period(Vessel vessel)
        {
            if (vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH)
                return vessel.mainBody.rotates ? vessel.mainBody.rotationPeriod : 0;
            Orbit orbit = vessel.orbit;
            return orbit != null && orbit.eccentricity < 1.0 ? orbit.period : 0;
        }

        private string What => _connected ? "signal loss" : "next contact";

        private void Finish(double ut)
        {
            _searching = false;
            string via = _connected ? null : _reachedVia;
            bool news = Result != Status.Found || Via != via || Math.Abs(ChangeUT - ut) > 60.0;
            Result = Status.Found;
            ChangeUT = ut;
            Via = via;
            if (news) Log.Info($"{What}: in {DelayService.Format(Math.Max(0, ut - _now))}{(via != null ? " via " + via : "")} ({_nodes.Count} nodes)");
        }

        /// <summary>
        /// Checks the model against CommNet, for the test harness: rebuilds the network so its
        /// positions are from now, then returns whether the model sees a path from
        /// <paramref name="vessel"/> and how far (m) its node positions are from CommNet's.
        /// NaN when the model doesn't cover this CommNet or the vessel.
        /// </summary>
        internal static double ModelCheck(Vessel vessel, out bool connected)
        {
            connected = false;
            CommNetwork network = CommNetNetwork.Instance?.CommNet;
            if (network == null || !Covers(network) || vessel?.connection?.Comm == null) return double.NaN;
            network.Rebuild();
            var model = new ContactPredictor { _now = Planetarium.GetUniversalTime() };
            model.Snapshot(network, vessel);
            if (model._start == null) return double.NaN;
            connected = model.Connected(model._now);
            double error = 0;
            foreach (Node node in model._nodes)
                error = Math.Max(error, (node.Position - node.Comm.precisePosition).magnitude);
            return error;
        }

        /// <summary>The first time in (<paramref name="before"/>, <paramref name="after"/>] the signal has changed, to within a second or so.</summary>
        private double Bisect(double before, double after)
        {
            string via = _reachedVia;
            for (int i = 0; i < Bisections && after - before > 1.0; i++)
            {
                double mid = (before + after) / 2.0;
                if (Connected(mid) != _connected) { after = mid; via = _reachedVia; }
                else before = mid;
            }
            _reachedVia = via;
            return after;
        }

        // ---- the network at a moment ----

        private void Snapshot(CommNetwork network, Vessel vessel)
        {
            _nodes.Clear();
            _start = null;
            var vesselFor = new Dictionary<CommNode, Vessel>();
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                CommNode comm = v?.connection?.Comm;
                if (comm != null && !vesselFor.ContainsKey(comm)) vesselFor.Add(comm, v);
            }
            var homeFor = new Dictionary<CommNode, CommNetHome>();
            foreach (CommNetHome home in UnityEngine.Object.FindObjectsOfType<CommNetHome>())
            {
                CommNode comm = HomeComm(home);
                if (comm != null && !homeFor.ContainsKey(comm)) homeFor.Add(comm, home);
            }

            for (int i = 0; i < network.Count; i++)
            {
                CommNode comm = network[i];
                if (comm == null || comm.antennaRelay.power + comm.antennaTransmit.power <= 0) continue;
                var node = new Node
                {
                    Comm = comm,
                    Relay = comm.antennaRelay.power > 0,
                    Target = comm.isControlSource,
                    MultiHop = comm.isControlSourceMultiHop,
                };
                if (vesselFor.TryGetValue(comm, out Vessel owner))
                {
                    node.Name = owner.GetDisplayName();
                    if (owner.LandedOrSplashed || owner.situation == Vessel.Situations.PRELAUNCH || owner.orbit == null)
                        OnSurface(node, owner.mainBody, owner.GetWorldPos3D());
                    else
                        node.Orbit = owner.orbit;
                }
                else if (comm.isHome)
                {
                    node.Name = KSP.Localization.Localizer.Format(comm.displayName);
                    if (homeFor.TryGetValue(comm, out CommNetHome home) && HomeBody(home) != null)
                    {
                        node.Body = HomeBody(home);
                        node.Lat = HomeLat(home);
                        node.Lon = HomeLon(home);
                        node.Alt = HomeAlt(home);
                    }
                    else OnSurface(node, NearestBody(comm.precisePosition), comm.precisePosition);
                }
                else continue; // neither a vessel nor a ground station: nothing to move it by
                if (node.Body == null && node.Orbit == null) continue;
                if (owner == vessel)
                {
                    node.Target = false;
                    _start = node;
                    _nodes.Insert(0, node);
                }
                else _nodes.Add(node);
            }
            if (_visited.Length < _nodes.Count) _visited = new bool[_nodes.Count];

            // The pairs a path could link: from the vessel or a relay (a ground station ends the
            // path) to a relay or a control source.
            _pairs.Clear();
            IRangeModel range = CommNetScenario.RangeModel;
            for (int i = 0; i < _nodes.Count; i++)
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    if (!(From(i) && To(j)) && !(From(j) && To(i))) continue;
                    CommNode x = _nodes[i].Comm, y = _nodes[j].Comm;
                    double reach = Math.Max(range.GetMaximumRange(x.antennaRelay.power, y.antennaRelay.power),
                                   Math.Max(range.GetMaximumRange(x.antennaRelay.power, y.antennaTransmit.power),
                                            range.GetMaximumRange(x.antennaTransmit.power, y.antennaRelay.power)));
                    _pairs.Add(new Pair { A = i, B = j, Range = reach });
                }

            _occluders.Clear();
            CommNetParams settings = HighLogic.CurrentGame?.Parameters?.CustomParams<CommNetParams>();
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                double multiplier = settings == null ? 1.0 : body.atmosphere ? settings.occlusionMultiplierAtm : settings.occlusionMultiplierVac;
                double radius = body.Radius * body.scaledRadiusHorizonMultiplier * multiplier;
                if (radius > 0) _occluders.Add(new Occluder { Body = body, Radius = radius });
            }
        }

        private bool From(int i) => i == 0 || (i > 0 && _nodes[i].Relay && !(_nodes[i].Target && _nodes[i].MultiHop));
        private bool To(int i) => i > 0 && (_nodes[i].Relay || _nodes[i].Target);

        private static void OnSurface(Node node, CelestialBody body, Vector3d position)
        {
            if (body == null) return;
            node.Body = body;
            UnityEngine.Vector2d latLon = body.GetLatitudeAndLongitude(position);
            node.Lat = latLon.x;
            node.Lon = latLon.y;
            node.Alt = body.GetAltitude(position);
        }

        private static CelestialBody NearestBody(Vector3d position)
        {
            CelestialBody best = null;
            double bestHeight = double.MaxValue;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                double height = Math.Abs((position - body.position).magnitude - body.Radius);
                if (height < bestHeight) { bestHeight = height; best = body; }
            }
            return best;
        }

        private Vector3d BodyAt(CelestialBody body, double ut)
        {
            if (!_bodyAt.TryGetValue(body, out Vector3d position))
            {
                position = body.orbit == null ? body.position : body.getTruePositionAtUT(ut);
                _bodyAt[body] = position;
            }
            return position;
        }

        private void PlaceAt(double ut)
        {
            _bodyAt.Clear();
            foreach (Node node in _nodes)
            {
                if (node.Orbit != null)
                {
                    node.Position = node.Orbit.getTruePositionAtUT(ut);
                    continue;
                }
                // A point on a turning body: after dt it's where the point dt's turn further east is now.
                CelestialBody body = node.Body;
                double turn = body.rotates && body.rotationPeriod != 0 ? 360.0 * (ut - _now) / body.rotationPeriod : 0;
                node.Position = body.GetWorldSurfacePosition(node.Lat, node.Lon + turn, node.Alt) - body.position + BodyAt(body, ut);
            }
            foreach (Occluder occluder in _occluders) occluder.Position = BodyAt(occluder.Body, ut);
        }

        /// <summary>Places everything at <paramref name="ut"/> and sets its velocity there.</summary>
        private void Move(double ut)
        {
            int count = _nodes.Count + _occluders.Count;
            if (_ahead.Length < count) _ahead = new Vector3d[count];
            PlaceAt(ut + VelocityDt);
            for (int i = 0; i < _nodes.Count; i++) _ahead[i] = _nodes[i].Position;
            for (int i = 0; i < _occluders.Count; i++) _ahead[_nodes.Count + i] = _occluders[i].Position;
            PlaceAt(ut);
            for (int i = 0; i < _nodes.Count; i++) _nodes[i].Velocity = (_ahead[i] - _nodes[i].Position) / VelocityDt;
            for (int i = 0; i < _occluders.Count; i++) _occluders[i].Velocity = (_ahead[_nodes.Count + i] - _occluders[i].Position) / VelocityDt;
        }

        /// <summary>
        /// The next step after <see cref="Move"/>: half the time before any pair could change,
        /// its distance from changing (range left, clearance over a body) over how fast that
        /// distance can shrink (relative speed), within the step limits.
        /// </summary>
        private double Step()
        {
            double safe = double.MaxValue;
            foreach (Pair pair in _pairs)
            {
                Node a = _nodes[pair.A], b = _nodes[pair.B];
                double rate = (a.Velocity - b.Velocity).magnitude;
                if (rate > 0) safe = Math.Min(safe, Math.Abs(pair.Range - (a.Position - b.Position).magnitude) / rate);
                foreach (Occluder occluder in _occluders)
                {
                    // The segment's distance from the body changes no faster than its ends move relative to it.
                    double speed = Math.Max((a.Velocity - occluder.Velocity).magnitude, (b.Velocity - occluder.Velocity).magnitude);
                    if (speed <= 0) continue;
                    safe = Math.Min(safe, Math.Abs(Clearance(occluder, a.Position, b.Position)) / speed);
                }
            }
            return Math.Max(_minStep, Math.Min(_maxStep, StepSafety * safe));
        }

        /// <summary>Whether a path from the vessel reaches a control source at <paramref name="ut"/>.</summary>
        private bool Connected(double ut)
        {
            PlaceAt(ut);
            return Reached();
        }

        /// <summary>Whether a path from the vessel reaches a control source, where everything is now placed.</summary>
        private bool Reached()
        {
            Array.Clear(_visited, 0, _visited.Length);
            _queue.Clear();
            _queue.Enqueue(0);
            _visited[0] = true;
            while (_queue.Count > 0)
            {
                int from = _queue.Dequeue();
                Node a = _nodes[from];
                for (int to = 1; to < _nodes.Count; to++)
                {
                    if (_visited[to]) continue;
                    Node b = _nodes[to];
                    bool target = b.Target && (b.MultiHop || from == 0);
                    if (!target && !b.Relay) continue;
                    if (!Linked(a, b)) continue;
                    if (target)
                    {
                        _reachedVia = b.Name;
                        return true;
                    }
                    _visited[to] = true;
                    _queue.Enqueue(to);
                }
            }
            return false;
        }

        private bool Linked(Node a, Node b)
        {
            double sqrDistance = (a.Position - b.Position).sqrMagnitude;
            IRangeModel range = CommNetScenario.RangeModel;
            CommNode x = a.Comm, y = b.Comm;
            if (!range.InRange(x.antennaRelay.power, y.antennaRelay.power, sqrDistance)
                && !range.InRange(x.antennaRelay.power, y.antennaTransmit.power, sqrDistance)
                && !range.InRange(x.antennaTransmit.power, y.antennaRelay.power, sqrDistance))
                return false;
            foreach (Occluder occluder in _occluders)
                if (Clearance(occluder, a.Position, b.Position) < 0) return false;
            return true;
        }

        /// <summary>How far (m) the segment from <paramref name="p"/> to <paramref name="q"/> passes outside the occluder's radius; negative when it's blocked.</summary>
        private static double Clearance(Occluder occluder, Vector3d p, Vector3d q)
        {
            Vector3d d = q - p;
            double length = d.sqrMagnitude;
            double along = length > 0 ? Vector3d.Dot(occluder.Position - p, d) / length : 0;
            along = along < 0 ? 0 : along > 1 ? 1 : along;
            return (p + d * along - occluder.Position).magnitude - occluder.Radius;
        }
    }
}
