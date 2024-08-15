using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        await Run1();
        await Run2();
    }
    static async Task Run1()
    {
        List<int> numbers = new List<int> { 1, 2, 3, 4, 5 };

        List<Task> tasks = new List<Task>();

        foreach (var number in numbers)
        {
            tasks.Add(Task.Run(async () =>
            {
                await DoWorkAsync(number);
            }));
        }

        await Task.WhenAll(tasks);
        Console.WriteLine("All tasks completed.");

    }

    static async Task Run2()
    {
        List<int> numbers = new List<int> { 1, 2, 3, 4, 5 };
        ConcurrentBag<Task> tasks = new ConcurrentBag<Task>();

        Parallel.ForEach(numbers, number =>
        {
            var task = DoWorkAsync(number);
            tasks.Add(task);
        });

        await Task.WhenAll(tasks);
        Console.WriteLine("All tasks completed.");
    }
    

    static async Task DoWorkAsync(int number)
    {
        // 비동기 작업 시뮬레이션 (예: 1초 동안 대기)
        await Task.Delay(1000);
        Console.WriteLine($"Completed number: {number}");
    }
}

