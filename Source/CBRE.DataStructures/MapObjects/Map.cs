using CBRE.Common;
using CBRE.DataStructures.GameData;
using CBRE.DataStructures.Geometric;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace CBRE.DataStructures.MapObjects
{
    [Serializable]
    public class Map : ISerializable
    {
        public decimal Version { get; set; }
        public List<Visgroup> Visgroups { get; private set; }
        public List<Camera> Cameras { get; private set; }
        public Camera ActiveCamera { get; set; }
        public World WorldSpawn { get; set; }
        public IDGenerator IDGenerator { get; private set; }

        public bool Show2DGrid { get; set; }
        public bool Show3DGrid { get; set; }
        public bool SnapToGrid { get; set; }
        public decimal GridSpacing { get; set; }
        public bool HideFaceMask { get; set; }
        public bool HideDisplacementSolids { get; set; }
        public bool HideToolTextures { get; set; }
        public bool HideEntitySprites { get; set; }
        public bool HideMapOrigin { get; set; }
        public bool IgnoreGrouping { get; set; }
        public bool TextureLock { get; set; }
        public bool TextureScalingLock { get; set; }
        public bool Cordon { get; set; }
        public Box CordonBounds { get; set; }

        public Map()
        {
            Version = 1;
            Visgroups = new List<Visgroup>();
            Cameras = new List<Camera>();
            ActiveCamera = null;
            IDGenerator = new IDGenerator();
            WorldSpawn = new World(IDGenerator.GetNextObjectID());

            Show2DGrid = SnapToGrid = true;
            TextureLock = true;
            HideDisplacementSolids = true;
            CordonBounds = new Box(Coordinate.One * -1024, Coordinate.One * 1024);
        }

        protected Map(SerializationInfo info, StreamingContext context)
        {
            Version = info.GetDecimal("Version");
            Visgroups = ((Visgroup[])info.GetValue("Visgroups", typeof(Visgroup[]))).ToList();
            Cameras = ((Camera[])info.GetValue("Cameras", typeof(Camera[]))).ToList();
            int activeCamera = info.GetInt32("ActiveCameraID");
            ActiveCamera = activeCamera >= 0 ? Cameras[activeCamera] : null;
            WorldSpawn = (World)info.GetValue("WorldSpawn", typeof(World));
            IDGenerator = (IDGenerator)info.GetValue("IDGenerator", typeof(IDGenerator));
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Version", Version);
            info.AddValue("Visgroups", Visgroups.ToArray());
            info.AddValue("Cameras", Cameras.ToArray());
            info.AddValue("ActiveCameraID", Cameras.IndexOf(ActiveCamera));
            info.AddValue("WorldSpawn", WorldSpawn);
            info.AddValue("IDGenerator", IDGenerator);
        }

        public IEnumerable<MapFeature> GetUsedFeatures()
        {
            var all = WorldSpawn.FindAll();
            
            bool hasSolids = false, hasEntities = false, hasGroups = false; bool hasDisplacements = false, hasMultiVis = false;

            foreach (var obj in all)
            {
                if (obj is Solid s) {
                    hasSolids = true;
                    if (!hasDisplacements && s.Faces.Any(f => f is Displacement)) hasDisplacements = true;
                }
                else if (obj is Entity) hasEntities = true;
                else if (obj is Group) hasGroups = true;

                if (obj.Visgroups.Count > 1) hasMultiVis = true;
            }

            if (hasSolids) yield return MapFeature.Solids;
            if (hasEntities) yield return MapFeature.Entities;
            if (hasGroups) yield return MapFeature.Groups;
            if (hasDisplacements) yield return MapFeature.Displacements;
            if (Visgroups.Any()) yield return MapFeature.SingleVisgroups;
            if (hasMultiVis) yield return MapFeature.MultipleVisgroups;
            if (Cameras.Count > 1) yield return MapFeature.Cameras;
        }

        public TransformFlags GetTransformFlags()
        {
            TransformFlags flags = TransformFlags.None;
            if (TextureLock) flags |= TransformFlags.TextureLock;
            if (TextureScalingLock) flags |= TransformFlags.TextureScalingLock;
            return flags;
        }

        public IEnumerable<string> GetAllTextures()
        {
            // HashSet сразу отсекает дубликаты и делает это быстро
            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            FillTexturesRecursive(WorldSpawn, textures);
            return textures;
        }

        private static void FillTexturesRecursive(MapObject obj, HashSet<string> set)
        {
            if (obj == null) return;
            
            if (obj is Entity ent && obj.ChildCount == 0)
            {
                if (ent.EntityData.Name == "infodecal")
                {
                    var tex = ent.EntityData.Properties.FirstOrDefault(x => x.Key == "texture");
                    if (tex != null) set.Add(tex.Value);
                }
            }
            else if (obj is Solid s)
            {
                foreach (var face in s.Faces)
                {
                    if (face.Texture != null) set.Add(face.Texture.Name);
                }
            }
            
            foreach (var child in obj.GetChildren())
            {
                FillTexturesRecursive(child, set);
            }
        }

        /// <summary>
        /// Should be called when a map is loaded. Sets up visgroups, object ids, gamedata, and textures.
        /// </summary>
        public void PostLoadProcess(GameData.GameData gameData, Func<string, ITexture> textureAccessor, Func<string, float> textureOpacity)
        {
            PartialPostLoadProcess(gameData, textureAccessor, textureOpacity);

            List<MapObject> all = WorldSpawn.FindAll();

            // Set maximum ids
            long maxObjectId = all.Max(x => x.ID);
            List<Face> faces = all.OfType<Solid>().SelectMany(x => x.Faces).ToList();
            long maxFaceId = faces.Any() ? faces.Max(x => x.ID) : 0;
            IDGenerator.Reset(maxObjectId, maxFaceId);

            // todo visgroups
            // WorldSpawn.ForEach(x => x.IsVisgroupHidden, x => x.IsVisgroupHidden = true, true);

            // Auto visgroup
            AutoVisgroup auto = AutoVisgroup.GetDefaultAutoVisgroup();
            AutoVisgroup quickHide = new AutoVisgroup { ID = int.MinValue, IsHidden = true, Name = "Autohide", Parent = auto, Visible = false };
            auto.Children.Add(quickHide);
            Visgroups.Add(auto);
            UpdateAutoVisgroups(all, false);

            // Purge empty groups
            foreach (MapObject emptyGroup in WorldSpawn.Find(x => x is Group && !x.HasChildren))
            {
                emptyGroup.SetParent(null);
            }
        }

        public void UpdateAutoVisgroups(MapObject node, bool recursive)
        {
            List<MapObject> nodes = recursive ? node.FindAll() : new List<MapObject> { node };
            UpdateAutoVisgroups(nodes, false);
        }

        public void UpdateAutoVisgroups(IEnumerable<MapObject> nodes, bool recursive)
        {
            if (nodes == null) return;

            var allVisgroups = GetAllVisgroups();
            if (allVisgroups == null) return;

            AutoVisgroup[] autos = allVisgroups.OfType<AutoVisgroup>().Where(x => x.Filter != null).ToArray();
            IEnumerable<MapObject> list = recursive ? nodes.SelectMany(x => x.FindAll()) : nodes;

            Parallel.ForEach(list, o =>
            {
                if (o == null || o.Visgroups == null || o.AutoVisgroups == null) return;

                o.Visgroups.RemoveAll(x => o.AutoVisgroups.Contains(x));
                o.AutoVisgroups.Clear();

                for (int i = 0; i < autos.Length; i++)
                {
                    AutoVisgroup vg = autos[i];
                    try 
                    {
                        if (vg.Filter(o))
                        {
                            Visgroup visgroup = vg;
                            while (visgroup != null)
                            {
                                if (o.AutoVisgroups.Contains(visgroup.ID)) break;
                                o.AutoVisgroups.Add(visgroup.ID);
                                visgroup = visgroup.Parent;
                            }
                        }
                    }
                    catch { }
                }
        
                o.Visgroups.AddRange(o.AutoVisgroups);
            });
        }

        public IEnumerable<Visgroup> GetAllVisgroups()
        {
            var result = new List<Visgroup>();
            if (Visgroups == null || !Visgroups.Any()) return result;
            
            var stack = new Stack<Visgroup>(Visgroups);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                result.Add(current);
                
                if (current.Children != null)
                {
                    foreach (var child in current.Children) stack.Push(child);
                }
            }
            return result;
        }

        public void PartialPostLoadProcess(GameData.GameData gameData, Func<string, ITexture> textureAccessor, Func<string, float> textureOpacity)
        {
            List<MapObject> objects = WorldSpawn.FindAll();
            Parallel.ForEach(objects, obj =>
            {
                if (obj is Entity)
                {
                    Entity ent = (Entity)obj;
                    if (ent.GameData == null || !String.Equals(ent.GameData.Name, ent.EntityData.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        GameDataObject gd =
                            gameData.Classes.FirstOrDefault(
                                x => String.Equals(x.Name, ent.EntityData.Name, StringComparison.CurrentCultureIgnoreCase) && x.ClassType != ClassType.Base);
                        ent.GameData = gd;
                        ent.UpdateBoundingBox();
                    }
                }
                else if (obj is Solid)
                {
                    Solid s = ((Solid)obj);
                    bool disp = HideDisplacementSolids && s.Faces.Any(x => x is Displacement);
                    s.Faces.ForEach(f =>
                    {
                        if (f.Texture.Texture == null)
                        {
                            f.Texture.Texture = textureAccessor(f.Texture.Name.ToLowerInvariant());
                            f.CalculateTextureCoordinates(true);
                        }
                        if (disp && !(f is Displacement))
                        {
                            f.Opacity = 0;
                        }
                        else if (f.Texture.Texture != null)
                        {
                            f.Opacity = textureOpacity(f.Texture.Name.ToLowerInvariant());
                            if (!HideToolTextures && f.Opacity < 0.1) f.Opacity = 1;
                        }
                    });
                }
            });
        }

        public Camera GetActiveCamera()
        {
            if (!Cameras.Any() || ActiveCamera == null) return null;
            return ActiveCamera;
        }
    }
}
