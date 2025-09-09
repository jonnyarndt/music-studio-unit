using System;
using System.Collections.Generic;
using System.Linq;
namespace musicStudioUnit
{
    internal class PassengerGroupExample
    {
        internal static void Main(string[] args)
        {
            var hosts = new List<Host>
            {
                new Host("00:00:00:00:00:01", "a1"),
                new Host("00:00:00:00:00:02", "a2"),
                new Host("00:00:00:00:00:03", "a3"),
                new Host("00:00:00:00:00:04", "a4"),
                new Host("00:00:00:00:00:05", "a5"),
                new Host("00:00:00:00:00:06", "a6"),
                new Host("00:00:00:00:00:07", "b1"),
                new Host("00:00:00:00:00:08", "b2"),
                new Host("00:00:00:00:00:09", "b3"),
                new Host("00:00:00:00:00:10", "b4"),
                new Host("00:00:00:00:00:11", "b5"),
                new Host("00:00:00:00:00:12", "b6"),
                new Host("00:00:00:00:00:13", "c1"),
                new Host("00:00:00:00:00:14", "c2"),
                new Host("00:00:00:00:00:15", "c3"),
                new Host("00:00:00:00:00:16", "c4"),
                new Host("00:00:00:00:00:17", "c5"),
                new Host("00:00:00:00:00:18", "c6"),
                new Host("00:00:00:00:00:19", "d1"),
                new Host("00:00:00:00:00:20", "d2"),
                new Host("00:00:00:00:00:21", "d3"),
                new Host("00:00:00:00:00:22", "d4"),
                new Host("00:00:00:00:00:23", "d5"),
                new Host("00:00:00:00:00:24", "d6"),
                new Host("00:00:00:00:00:25", "e1"),
                new Host("00:00:00:00:00:26", "e2"),
                new Host("00:00:00:00:00:27", "e3"),
                new Host("00:00:00:00:00:28", "e4"),
                new Host("00:00:00:00:00:29", "e5"),
                new Host("00:00:00:00:00:30", "e6")
            };
        }
    }
    
    /// <summary>
    /// Host class to represent a host with MAC address and seating assignment.
    /// </summary>
    internal class Host
    {
        internal string MACAddress { get; set; }
        internal string SeatingAssignment
        {
            get => seatingAssignment;
            set
            {
                if (value.Length != 2)
                {
                    throw new ArgumentException("SeatingAssignment must be exactly two characters long.");
                }
                if (!char.IsLetter(value[0]) || !char.IsDigit(value[1]))
                {
                    throw new ArgumentException("SeatingAssignment must start with a letter and end with a number.");
                }
                seatingAssignment = value.ToLower();
            }
        }

        internal Host(string macAddress, string seatingAssignment)
        {
            MACAddress = macAddress;
            SeatingAssignment = seatingAssignment;
        }
    }

    /// <summary>
    /// HostGroup class to represent a group of hosts with a common seating assignment.
    /// </summary>
    internal class HostGroup
    {
        internal List<Host> Hosts { get; set; } = new List<Host>();
        internal Host MainHost { get; private set; }
    }

    /// <summary>
    /// HostManager class to manage host groups based on seating assignments.
    /// </summary>
    internal class HostManager
    {
        internal List<HostGroup> HostGroups { get; set; } = new List<HostGroup>();

        internal void CreateGroups(List<Host> hosts)
        {
            var grid = new Dictionary<string, Host>();
            foreach (var host in hosts)
            {
                grid[host.SeatingAssignment] = host;
            }

            var visited = new HashSet<string>();
            foreach (var host in hosts)
            {
                if (!visited.Contains(host.SeatingAssignment))
                {
                    var group = new HostGroup();
                    DFS(host.SeatingAssignment, grid, visited, group);
                    HostGroups.Add(group);
                }
            }
            DetermineMainHost();
        }

        /// <summary>
        /// Depth First Search algorithm to find all connected hosts or hosts with a common seating assignment.
        /// </summary>
        /// <param name="seat">seat to be processed, example: A1.</param>
        /// <param name="grid">dictionary mapping seat identifier.</param>
        /// <param name="visited">set of visited seats to avoid processing same seats.</param>
        /// <param name="group">object that collects hosts belonging to the same group.</param>
        private void DFS(string seat, Dictionary<string, Host> grid, HashSet<string> visited, HostGroup group)
        {
            // check if current seat exists in grid and if it has already been visited.
            if (!grid.ContainsKey(seat) || visited.Contains(seat))
                return;

            // if seat valid and unvisited, then add to visited set,
            // and add the corresponding seats' Host object from the grid to the group.
            visited.Add(seat);
            group.Hosts.Add(grid[seat]);

            // get the column and row from the seat identifier.
            var (col, row) = (seat[0], int.Parse(seat[1].ToString()));
            var directions = new (char, int)[] { ('A', 1), ('A', -1), ('B', 0), ('C', 0) };

            // iterate over the directions to find all connected seats.
            foreach (var (dCol, dRow) in directions)
            {
                var newCol = (char)(col + dCol);
                var newRow = row + dRow;
                var newSeat = $"{newCol}{newRow}";

                // check if the new seat is within the grid.
                if (newCol >= 'a' && newCol <= 'e' && newRow >= 1 && newRow <= 6)
                {
                    // recursively call DFS on the new seat 
                    // which will add all connected seats to the group.
                    DFS(newSeat, grid, visited, group);
                }
            }
        }


        /// <summary>
        /// Determines the single MainHost over all hosts after all groups have been formed.
        /// </summary>
        private void DetermineMainHost()
        {
            var allHosts = HostGroups.SelectMany(g => g.Hosts);
            if (allHosts.Any())
            {
                MainHost = allHosts.OrderBy(h => h.MACAddress).First();
            }
        }

        /// <summary>
        /// Determines if a host is the MainHost
        /// </summary>
        internal bool IsMainHost(Host host)
        {
            return MainHost != null && MainHost.SeatingAssignment == host.SeatingAssignment;
        }

        /// <summary>
        /// Determines if a host is the MainHost based on the seating assignment.
        /// </summary>
        internal bool IsMainHostSeatingAssignment(string seatingAssignment)
        {
            return MainHost != null && MainHost.SeatingAssignment == seatingAssignment;
        }

        /// <summary>
        /// Determines if a host is the MainHost based on provided MAC address.
        /// </summary>
        internal bool IsMainHostMACAddress(string macAddress)
        {
            return MainHost != null && MainHost.MACAddress == macAddress;
        }
    }
}