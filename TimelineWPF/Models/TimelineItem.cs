namespace TimelineWPF.Models
{
    public enum TimelineItemType { State, Event, Action }

    public class TimelineItem
    {
        public long Time { get; set; } // microseconds
        public TimelineItemType Type { get; set; }
        public required string Name { get; set; }
        public long Duration { get; set; } // microseconds, only for State
    }
}