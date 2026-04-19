using CBRE.DataStructures.MapObjects.VisgroupFilters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CBRE.DataStructures.MapObjects
{
    public class AutoVisgroup : Visgroup
    {
        public bool IsHidden { get; set; }
        public Func<MapObject, bool> Filter { get; set; }
        public override bool IsAutomatic => true;
        
        private static List<IVisgroupFilter> _cachedFilters;

        public override Visgroup Clone()
        {
            return new AutoVisgroup
            {
                ID = ID,
                Name = Name,
                Visible = Visible,
                Colour = Colour,
                Children = Children.Select(x => x.Clone()).ToList(),
                IsHidden = IsHidden,
                Filter = Filter
            };
        }

        public static AutoVisgroup GetDefaultAutoVisgroup()
        {
            if (_cachedFilters == null)
            {
                _cachedFilters = typeof(IVisgroupFilter).Assembly.GetTypes()
                    .Where(x => typeof(IVisgroupFilter).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
                    .Select(Activator.CreateInstance)
                    .OfType<IVisgroupFilter>()
                    .ToList();
            }
            
            int i = -1;
            AutoVisgroup auto = new AutoVisgroup
            {
                ID = i--,
                IsHidden = false,
                Name = "Auto",
                Visible = true
            };
            foreach (IVisgroupFilter f in _cachedFilters)
            {
                AutoVisgroup parent = auto.Children.OfType<AutoVisgroup>().FirstOrDefault(x => x.Name == f.Group);
                if (parent == null)
                {
                    parent = new AutoVisgroup
                    {
                        ID = i--,
                        IsHidden = false,
                        Name = f.Group,
                        Visible = true,
                        Parent = auto
                    };
                    auto.Children.Add(parent);
                }
                AutoVisgroup vg = new AutoVisgroup
                {
                    ID = i--,
                    Filter = f.IsMatch,
                    IsHidden = false,
                    Name = f.Name,
                    Visible = true,
                    Parent = parent
                };
                parent.Children.Add(vg);
            }
            return auto;
        }
    }
}