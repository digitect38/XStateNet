using System.Collections.Generic;

namespace TimelineWPF.Models
{
    public class StateMachineDefinition
    {
        public required string Name { get; set; }
        public required List<string> States { get; set; }
        public required string InitialState { get; set; }
        public List<TimelineItem> Data { get; set; } = new List<TimelineItem>();
    }
}