using System;
using System.Collections.Generic;
using System.Linq;
using CommNet;

namespace LightLag
{
    /// <summary>
    /// Estimates when a vessel without a signal next gets one: CommNet only knows who can reach
    /// whom right now, so LightLag steps forward in time and redoes the link checks itself.
    ///
    /// <para><b>Model:</b> stock CommNet's rules, applied to where everything will be. Every
    /// node keeps the antenna powers CommNet gave it now. Vessels in flight follow their current
    /// orbit (they coast), landed vessels and ground stations turn with their body. A link needs
    /// <see cref="CommNetScenario.RangeModel"/> to put the pair in range and no body in the way;
    /// a body blocks a radius of its radius × <c>scaledRadiusHorizonMultiplier</c> × CommNet's
    /// occlusion multiplier (vacuum or atmosphere), like stock's horizon culling (as a sphere).
    /// A path can pass through nodes with relay antennas and must end at a control source:
    /// a ground station or a probe control point, as stock's control path does.</para>
    ///
    /// <para><b>Not modelled:</b> burns and staging, plasma blackout, power running out, and
    /// vessels changing sphere of influence (the search stops at the end of the vessel's orbit
    /// patch). If the model says the vessel is connected now, something outside it (plasma,
    /// say) is cutting the signal, so there's no estimate.</para>
    ///
    /// <para><b>Stock CommNet only.</b> In RealAntennas mode (RealAntennas loaded) it's off,
    /// and so on any other network that replaces stock's: their links follow other rules.
    /// RealAntennas gets its own model with the adapter (docs/DESIGN.md, Q9).</para>
    ///
    /// <para><b>Search:</b> samples up to a horizon (two of the vessel's orbits or its body's
    /// days, from an hour up to 50 days), a few dozen samples per frame so it doesn't stall the
    /// game, then bisects the first gap to the second. It starts over every few seconds of real
    /// time, keeping the last answer until the new one is ready.</para>
    /// </summary>
    internal sealed class ContactPredictor
    {
        internal enum Status
        {
            /// <summary>Not searched yet, or the model can't explain the current outage.</summary>
            Unknown,
            /// <summary>Off: RealAntennas mode, or another CommNet the model doesn't cover. Nothing is shown.</summary>
            Off,
            Found,
            NotFound,
        }

        private const int Samples = 600, SamplesPerFrame = 40, Bisections = 14;
        private const float RestartSeconds = 5f;
        private const double MinHorizon = 3600.0, MaxHorizonDays = 50.0;

        public Status Result { get; private set; } = Status.Unknown;
        /// <summary>When the contact is expected (Found).</summary>
        public double ContactUT { get; private set; }
        /// <summary>The control source reached first (Found).</summary>
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
            public Vector3d Position;
        }

        private sealed class Occluder
        {
            public CelestialBody Body;
            public double Radius;
            public Vector3d Position;
        }

        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Occluder> _occluders = new List<Occluder>();
        private readonly Dictionary<CelestialBody, Vector3d> _bodyAt = new Dictionary<CelestialBody, Vector3d>();
        private readonly Queue<int> _queue = new Queue<int>();
        private bool[] _visited = new bool[0];
        private Node _start;
        private Guid _vesselId;
        private double _now, _end, _step, _t;
        private bool _searching;
        private float _restartAt;
        private string _reachedVia;
        private static bool _loggedOff;
        private static bool? _realAntennas;

        /// <summary>RealAntennas is loaded, so it runs CommNet (whether or not its network is up yet).</summary>
        internal static bool RealAntennasMode =>
            _realAntennas ?? (_realAntennas = AssemblyLoader.loadedAssemblies.Any(a => a.name == "RealAntennas")).Value;

        /// <summary>The model covers stock's own network only, and never in RealAntennas mode.</summary>
        private static bool Covers(CommNetwork network) => !RealAntennasMode && network.GetType() == typeof(CommNetwork);
        private Guid _loggedUnknown;

        /// <summary>Clears the answer, as when the vessel has a signal again.</summary>
        public void Reset()
        {
            Result = Status.Unknown;
            _searching = false;
            _vesselId = Guid.Empty;
            _restartAt = 0f;
        }

        /// <summary>Every frame while the vessel has no signal: advances the search a little.</summary>
        public void Update(Vessel vessel)
        {
            if (vessel == null) { Reset(); return; }
            if (vessel.id != _vesselId) { Reset(); _vesselId = vessel.id; }
            if (!_searching)
            {
                if (UnityEngine.Time.unscaledTime < _restartAt) return;
                _restartAt = UnityEngine.Time.unscaledTime + RestartSeconds;
                Begin(vessel);
                if (!_searching) return;
            }

            for (int i = 0; i < SamplesPerFrame && _searching; i++)
            {
                double previous = _t;
                _t = Math.Min(_t + _step, _end);
                if (Connected(_t))
                {
                    Finish(Bisect(previous, _t));
                    return;
                }
                if (_t >= _end)
                {
                    _searching = false;
                    if (Result != Status.NotFound) Log.Info($"next contact: none in the next {DelayService.Format(_end - _now)}");
                    Result = Status.NotFound;
                    Horizon = _end - _now;
                }
            }
        }

        private void Begin(Vessel vessel)
        {
            CommNetwork network = CommNetNetwork.Instance?.CommNet;
            if (network == null || vessel.connection?.Comm == null) { Result = Status.Unknown; return; }
            if (!Covers(network))
            {
                if (!_loggedOff)
                    Log.Info(RealAntennasMode ? "next contact: off in RealAntennas mode"
                                              : $"next contact: off on {network.GetType().FullName}");
                _loggedOff = true;
                Result = Status.Off;
                return;
            }

            _now = Planetarium.GetUniversalTime();
            Snapshot(network, vessel);
            if (_start == null) { Result = Status.Unknown; return; }

            // If the model already sees a path, what's cutting the signal is outside it.
            if (Connected(_now))
            {
                if (Result != Status.Unknown || _vesselId != _loggedUnknown)
                    Log.Info($"next contact: the model sees a path to {_reachedVia} now, so something else cuts the signal; no estimate");
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
            _step = (_end - _now) / Samples;
            _t = _now;
            _searching = _step > 0;
            if (!_searching) Result = Status.NotFound;
        }

        /// <summary>One orbit, or one day of the body it sits on.</summary>
        private static double Period(Vessel vessel)
        {
            if (vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH)
                return vessel.mainBody.rotates ? vessel.mainBody.rotationPeriod : 0;
            Orbit orbit = vessel.orbit;
            return orbit != null && orbit.eccentricity < 1.0 ? orbit.period : 0;
        }

        private void Finish(double ut)
        {
            _searching = false;
            bool news = Result != Status.Found || Via != _reachedVia || Math.Abs(ContactUT - ut) > 60.0;
            Result = Status.Found;
            ContactUT = ut;
            Via = _reachedVia;
            if (news) Log.Info($"next contact: in {DelayService.Format(ut - _now)} via {Via} ({_nodes.Count} nodes)");
        }

        /// <summary>
        /// Checks the model against CommNet now, for the test harness: whether the model sees a
        /// path from <paramref name="vessel"/> and how far (m) its node positions are from
        /// CommNet's. NaN when the model doesn't cover this CommNet or the vessel.
        /// </summary>
        internal static double ModelCheck(Vessel vessel, out bool connected)
        {
            connected = false;
            CommNetwork network = CommNetNetwork.Instance?.CommNet;
            if (network == null || !Covers(network) || vessel?.connection?.Comm == null) return double.NaN;
            var model = new ContactPredictor { _now = Planetarium.GetUniversalTime() };
            model.Snapshot(network, vessel);
            if (model._start == null) return double.NaN;
            connected = model.Connected(model._now);
            double error = 0;
            foreach (Node node in model._nodes)
                error = Math.Max(error, (node.Position - node.Comm.precisePosition).magnitude);
            return error;
        }

        /// <summary>The first connected time in (<paramref name="off"/>, <paramref name="on"/>], to within a second or so.</summary>
        private double Bisect(double off, double on)
        {
            string via = _reachedVia;
            for (int i = 0; i < Bisections && on - off > 1.0; i++)
            {
                double mid = (off + on) / 2.0;
                if (Connected(mid)) { on = mid; via = _reachedVia; }
                else off = mid;
            }
            _reachedVia = via;
            return on;
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
                    OnSurface(node, NearestBody(comm.precisePosition), comm.precisePosition);
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

            _occluders.Clear();
            CommNetParams settings = HighLogic.CurrentGame?.Parameters?.CustomParams<CommNetParams>();
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                double multiplier = settings == null ? 1.0 : body.atmosphere ? settings.occlusionMultiplierAtm : settings.occlusionMultiplierVac;
                double radius = body.Radius * body.scaledRadiusHorizonMultiplier * multiplier;
                if (radius > 0) _occluders.Add(new Occluder { Body = body, Radius = radius });
            }
        }

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

        /// <summary>Whether a path from the vessel reaches a control source at <paramref name="ut"/>.</summary>
        private bool Connected(double ut)
        {
            PlaceAt(ut);
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
                if (Blocks(occluder, a.Position, b.Position)) return false;
            return true;
        }

        /// <summary>Whether the segment from <paramref name="p"/> to <paramref name="q"/> passes within the occluder's radius.</summary>
        private static bool Blocks(Occluder occluder, Vector3d p, Vector3d q)
        {
            Vector3d d = q - p;
            double length = d.sqrMagnitude;
            double along = length > 0 ? Vector3d.Dot(occluder.Position - p, d) / length : 0;
            along = along < 0 ? 0 : along > 1 ? 1 : along;
            return (p + d * along - occluder.Position).sqrMagnitude < occluder.Radius * occluder.Radius;
        }
    }
}
