using System.Collections.Concurrent;

namespace SemiStandard.Simulator.Wpf
{
    public enum LotPriority
    {
        Normal = 0,
        Rush = 1,
        HotLot = 2,
        SuperHot = 3
    }

    public enum LotState
    {
        Waiting,
        Queued,
        Processing,
        Completed,
        OnHold
    }

    public class Lot
    {
        public string LotId { get; set; } = "";
        public string RecipeId { get; set; } = "";
        public int WaferCount { get; set; } = 25;
        public LotPriority Priority { get; set; } = LotPriority.Normal;
        public LotState State { get; set; } = LotState.Waiting;
        public DateTime ArrivalTime { get; set; } = DateTime.Now;
        public DateTime? DueDate { get; set; }
        public int ProcessingTimeMinutes { get; set; } = 60;
        public double Score { get; set; } // Calculated scheduling score

        // Manufacturing metrics
        public int StepsCompleted { get; set; } = 0;
        public int TotalSteps { get; set; } = 10;
        public string ProductType { get; set; } = "LOGIC";
        public string Technology { get; set; } = "7nm";
        public string CustomerCode { get; set; } = "CUST001";
    }

    public class SchedulingRule
    {
        public string Name { get; set; } = "";
        public double Weight { get; set; } = 1.0;
        public Func<Lot, double> ScoreFunction { get; set; } = lot => 0;
    }

    public class ManufacturingScheduler
    {
        private readonly List<Lot> _lotQueue = new();
        private readonly List<SchedulingRule> _rules = new();
        private readonly Random _random = new Random();

        // Scheduling parameters
        public bool EnableDynamicScheduling { get; set; } = true;
        public bool ConsiderSetupTime { get; set; } = true;
        public bool EnableBatching { get; set; } = true;

        public ManufacturingScheduler()
        {
            InitializeSchedulingRules();
        }

        private void InitializeSchedulingRules()
        {
            // Critical Ratio (due date urgency)
            _rules.Add(new SchedulingRule
            {
                Name = "Critical Ratio",
                Weight = 2.0,
                ScoreFunction = lot =>
                {
                    if (!lot.DueDate.HasValue) return 0;
                    var timeRemaining = (lot.DueDate.Value - DateTime.Now).TotalHours;
                    var processingTime = lot.ProcessingTimeMinutes / 60.0;
                    if (timeRemaining <= 0) return 100; // Overdue
                    return Math.Min(10, processingTime / timeRemaining * 10);
                }
            });

            // Priority Level
            _rules.Add(new SchedulingRule
            {
                Name = "Priority",
                Weight = 3.0,
                ScoreFunction = lot => (int)lot.Priority * 25
            });

            // First In First Out (age in queue)
            _rules.Add(new SchedulingRule
            {
                Name = "FIFO",
                Weight = 1.0,
                ScoreFunction = lot =>
                {
                    var age = (DateTime.Now - lot.ArrivalTime).TotalMinutes;
                    return Math.Min(10, age / 60); // Max 10 points after 10 hours
                }
            });

            // Shortest Processing Time
            _rules.Add(new SchedulingRule
            {
                Name = "SPT",
                Weight = 0.5,
                ScoreFunction = lot => 10 - Math.Min(10, lot.ProcessingTimeMinutes / 12.0)
            });

            // Setup Time Minimization (same recipe batching)
            _rules.Add(new SchedulingRule
            {
                Name = "Setup Minimization",
                Weight = 1.5,
                ScoreFunction = lot =>
                {
                    if (!ConsiderSetupTime) return 0;
                    // Check if previous lot had same recipe
                    var lastProcessed = _lotQueue.FirstOrDefault(l => l.State == LotState.Completed);
                    if (lastProcessed != null && lastProcessed.RecipeId == lot.RecipeId)
                        return 8; // Bonus for same recipe
                    return 0;
                }
            });
        }

        public void AddLot(Lot lot)
        {
            lot.ArrivalTime = DateTime.Now;
            _lotQueue.Add(lot);
            Logger.Log($"[SCHEDULER] Added lot {lot.LotId} with priority {lot.Priority}");
        }

        public void RemoveLot(string lotId)
        {
            _lotQueue.RemoveAll(l => l.LotId == lotId);
        }

        public List<Lot> GetScheduledQueue()
        {
            // Calculate scores for all waiting lots
            var waitingLots = _lotQueue.Where(l => l.State == LotState.Waiting).ToList();

            foreach (var lot in waitingLots)
            {
                lot.Score = CalculateLotScore(lot);
            }

            // Sort by score (highest first)
            var scheduled = waitingLots.OrderByDescending(l => l.Score).ToList();

            // Apply batching if enabled
            if (EnableBatching)
            {
                scheduled = ApplyBatching(scheduled);
            }

            return scheduled;
        }

        private double CalculateLotScore(Lot lot)
        {
            double totalScore = 0;
            double totalWeight = 0;

            foreach (var rule in _rules)
            {
                var score = rule.ScoreFunction(lot);
                totalScore += score * rule.Weight;
                totalWeight += rule.Weight;
            }

            return totalWeight > 0 ? totalScore / totalWeight : 0;
        }

        private List<Lot> ApplyBatching(List<Lot> lots)
        {
            // Group consecutive lots with same recipe
            var batched = new List<Lot>();
            var groups = lots.GroupBy(l => l.RecipeId);

            foreach (var group in groups.OrderByDescending(g => g.Max(l => l.Score)))
            {
                batched.AddRange(group);
            }

            return batched;
        }

        public Lot? GetNextLot()
        {
            var scheduled = GetScheduledQueue();
            return scheduled.FirstOrDefault();
        }

        public void UpdateLotState(string lotId, LotState newState)
        {
            var lot = _lotQueue.FirstOrDefault(l => l.LotId == lotId);
            if (lot != null)
            {
                lot.State = newState;
                Logger.Log($"[SCHEDULER] Lot {lotId} state changed to {newState}");
            }
        }

        public List<Lot> GetAllLots()
        {
            return _lotQueue.ToList();
        }

        public ConcurrentDictionary<string, object> GetSchedulerMetrics()
        {
            var metrics = new ConcurrentDictionary<string, object>
            {
                ["TotalLots"] = _lotQueue.Count,
                ["WaitingLots"] = _lotQueue.Count(l => l.State == LotState.Waiting),
                ["ProcessingLots"] = _lotQueue.Count(l => l.State == LotState.Processing),
                ["CompletedLots"] = _lotQueue.Count(l => l.State == LotState.Completed),
                ["HotLots"] = _lotQueue.Count(l => l.Priority >= LotPriority.HotLot),
                ["AverageWaitTime"] = _lotQueue
                    .Where(l => l.State == LotState.Waiting)
                    .Select(l => (DateTime.Now - l.ArrivalTime).TotalMinutes)
                    .DefaultIfEmpty(0)
                    .Average(),
                ["OverdueLots"] = _lotQueue.Count(l => l.DueDate.HasValue && l.DueDate < DateTime.Now)
            };

            return metrics;
        }

        // Generate test lots for demonstration
        public void GenerateTestLots(int count = 5)
        {
            var recipes = new[] { "ASML-193nm-DUV", "ASML-EUV-13.5nm", "ASML-ArF-Immersion" };
            var priorities = Enum.GetValues<LotPriority>();

            for (int i = 0; i < count; i++)
            {
                var lot = new Lot
                {
                    LotId = $"LOT{1000 + _lotQueue.Count:D4}",
                    RecipeId = recipes[_random.Next(recipes.Length)],
                    WaferCount = 25,
                    Priority = priorities[_random.Next(priorities.Length)],
                    State = LotState.Waiting,
                    ProcessingTimeMinutes = 45 + _random.Next(30),
                    DueDate = DateTime.Now.AddHours(2 + _random.Next(24)),
                    ProductType = _random.Next(2) == 0 ? "LOGIC" : "MEMORY",
                    Technology = _random.Next(3) == 0 ? "5nm" : _random.Next(2) == 0 ? "7nm" : "10nm",
                    CustomerCode = $"CUST{_random.Next(1, 6):D3}",
                    StepsCompleted = _random.Next(5),
                    TotalSteps = 10 + _random.Next(10)
                };

                AddLot(lot);
            }
        }
    }
}