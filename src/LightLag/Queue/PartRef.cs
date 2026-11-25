using System.Collections.Generic;

namespace LightLag
{
    /// <summary>
    /// A saved reference to a part (by <c>persistentId</c>) and optionally one of its modules
    /// (by index, checked against the class name). Resolved again when a command runs.
    /// </summary>
    public struct PartRef
    {
        public uint PartId;

        /// <summary>Index in <c>part.Modules</c>, or -1 for the part itself.</summary>
        public int ModuleIndex;

        public string ModuleClass;

        public static PartRef Of(Part part, PartModule module) => new PartRef
        {
            PartId = part.persistentId,
            ModuleIndex = module != null ? part.Modules.IndexOf(module) : -1,
            ModuleClass = module != null ? module.ClassName : null,
        };

        public bool Same(PartRef other) => PartId == other.PartId && ModuleIndex == other.ModuleIndex;

        /// <summary>Finds the part on <paramref name="vessel"/>, or null.</summary>
        public Part FindPart(Vessel vessel)
        {
            List<Part> parts = vessel?.parts;
            if (parts == null) return null;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].persistentId == PartId) return parts[i];
            return null;
        }

        /// <summary>The module, or null if the part or module is gone (or is the part itself).</summary>
        public PartModule FindModule(Part part)
        {
            if (part == null || ModuleIndex < 0 || ModuleIndex >= part.Modules.Count) return null;
            PartModule module = part.Modules[ModuleIndex];
            return module != null && module.ClassName == ModuleClass ? module : null;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("part", PartId);
            node.AddValue("module", ModuleIndex);
            if (ModuleClass != null) node.AddValue("moduleClass", ModuleClass);
        }

        public static bool TryLoad(ConfigNode node, out PartRef target)
        {
            target = new PartRef { ModuleIndex = -1 };
            if (!uint.TryParse(node.GetValue("part"), out target.PartId)) return false;
            if (!int.TryParse(node.GetValue("module"), out target.ModuleIndex)) return false;
            target.ModuleClass = node.GetValue("moduleClass");
            return true;
        }
    }
}
