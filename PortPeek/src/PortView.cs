using System;
using System.Collections.Generic;
using System.Linq;

namespace PortPeek
{
    internal static class PortView
    {
        internal static PortEntry[] FilterSort(IEnumerable<PortEntry> entries, string search, string protocol, int column, bool ascending, FavoriteStore favorites = null)
        {
            string query = (search ?? "").Trim();
            var rows = (entries ?? new PortEntry[0]).Where(e => e != null &&
                (String.IsNullOrEmpty(protocol) || String.Equals(e.Protocol, protocol, StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 || (e.Port + " " + e.ProcessId + " " + e.ProcessName + " " + e.Protocol + " " + e.BindingDisplay + " " + String.Join(" ", e.Addresses ?? new string[0])).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            Func<PortEntry, IComparable> value;
            switch (column)
            {
                case 1: value = e => (e.ProcessName ?? "").ToUpperInvariant(); break;
                case 2: value = e => e.ProcessId; break;
                case 3: value = e => e.Protocol; break;
                case 4: value = e => e.BindingDisplay; break;
                default: value = e => e.Port; break;
            }
            var pinned = rows.OrderByDescending(e => favorites != null && favorites.IsFavorite(e));
            return (ascending ? pinned.ThenBy(value) : pinned.ThenByDescending(value))
                .ThenBy(e => e.Port).ThenBy(e => e.Protocol).ThenBy(e => e.ProcessId).ToArray();
        }
    }
}
