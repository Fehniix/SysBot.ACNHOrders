using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using System.Threading;

namespace SysBot.ACNHOrders
{
    public class VisitorDifference 
    {
        public class Difference
        {
            public string Name;
            public bool Arrived; // (new) Otherwise left
            public Difference (string n, bool a) { Name = n; Arrived = a; }
        }

        public List<string> Visitors { get; private set; }

        public VisitorDifference(IReadOnlyCollection<string> str) { Visitors = new List<string>(str); }

        public List<Difference> GetDifferenceWith(VisitorDifference other)
        {
            List<Difference> diffs = new List<Difference>();
            foreach (var visitor in other.Visitors)
                if (visitor != string.Empty && !Visitors.Contains(visitor))
                    diffs.Add(new Difference(visitor, true));
            foreach (var currentVisitor in Visitors)
                if (currentVisitor != string.Empty && !other.Visitors.Contains(currentVisitor))
                    diffs.Add(new Difference(currentVisitor, false));
            return diffs;
        }
    }

    /// <summary>
    /// Identifies uniquely a visitor that joined or left the island.
    /// </summary>
    [Serializable]
    public class UniqueVisitor 
    {
        /// <summary>
        /// The visitor's name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// The Nintendo ID associated to the visitor.
        /// </summary>
        public string? nintendoID { get; set; }

        /// <summary>
        /// The visitor's island name.
        /// </summary>
        public string islandName { get; set; }

        /// <summary>
        /// The visitor's island ID.
        /// </summary>
        public string? islandID { get; set; }

        public UniqueVisitor(string name, string islandName)
        {
            this.name = name;
            this.islandName = islandName;
        }

        public UniqueVisitor(string name, string islandName, string? nintendoID, string? islandID)
        {
            this.name = name;
            this.nintendoID = nintendoID;
            this.islandName = islandName;
            this.islandID = islandID;
        }

        public override string ToString()
        {
            return $"<UniqueVisitor | NID: {this.nintendoID}> {this.name}, from {this.islandName} (island ID: {this.islandID})";
        }
    }

    public class VisitorListHelper
    {
        private const int VisitorNameSize = 0x14;
        public const int VisitorListSize = 8;

        public string VisitorFormattedString { get; private set; } = "Names not loaded.";

        private readonly ISwitchConnectionAsync Connection;
        private readonly CrossBot BotRunner;
        private readonly CrossBotConfig Config;

        public string[] Visitors { get; private set; } = new string[VisitorListSize];

        public UniqueVisitor[] UniqueVisitors { get; set; } = new UniqueVisitor[VisitorListSize];

        public uint VisitorCount { get; private set; } = 0;
        public string TownName { get; private set; } = "the island";
        public VisitorDifference LastVisitorDiff { get; private set; }

        public VisitorListHelper(CrossBot bot)
        {
            BotRunner = bot;
            Connection = BotRunner.SwitchConnection;
            Config = BotRunner.Config;
            LastVisitorDiff = new VisitorDifference(Visitors);
        }

        public void SetTownName(string townName) => TownName = townName;
        
        public async Task<IReadOnlyCollection<VisitorDifference.Difference>> UpdateNames(CancellationToken token)
        {
            var formattedList = $"The following visitors are on {TownName}:\n";
            VisitorCount = 0;
            for (uint i = 0; i < VisitorListSize; ++i)
            {
                ulong offset = OffsetHelper.OnlineSessionVisitorAddress - (i * OffsetHelper.OnlineSessionVisitorSize);
                var bytes = await Connection.ReadBytesAsync((uint)offset, VisitorNameSize, token).ConfigureAwait(false);
                Visitors[i] = Encoding.UTF8.GetString(bytes).TrimEnd('\0');

                string VisitorInformation = "Available slot";
                if (!string.IsNullOrWhiteSpace(Visitors[i]))
                {
                    VisitorCount++;
                    VisitorInformation = Visitors[i];
                }

                formattedList += $"#{i + 1}: {VisitorInformation}\n";
            }

            VisitorFormattedString = formattedList;

            VisitorDifference currentVisitors = new VisitorDifference(Visitors);
            var toRet = LastVisitorDiff.GetDifferenceWith(currentVisitors);
            LastVisitorDiff = currentVisitors;
            return toRet;
        }

        public async Task<string[]> FetchVisitors(CancellationToken token)
        {
            string[] visitors = new string[VisitorListSize];

            for (uint i = 0; i < VisitorListSize; ++i)
            {
                ulong offset = OffsetHelper.OnlineSessionVisitorAddress - (i * OffsetHelper.OnlineSessionVisitorSize);
                var bytes = await Connection.ReadBytesAsync((uint)offset, VisitorNameSize, token).ConfigureAwait(false);
                visitors[i] = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            }

            return visitors;
        }
    }
}
