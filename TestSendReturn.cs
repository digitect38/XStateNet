using System;
using System.Threading.Tasks;
using XStateNet;

public class TestSendReturn
{
    public static async Task Main()
    {
        var json = @"{
            'id': 'testMachine',
            'initial': 'red',
            'states': {
                'red': {
                    'on': {
                        'GO': 'yellow'
                    }
                },
                'yellow': {
                    'on': {
                        'STOP': 'red'
                    }
                }
            }
        }";

        var machine = new StateMachine();
        StateMachine.ParseStateMachine(machine, json, false, null, null, null, null, null);
        machine.Start();

        Console.WriteLine($"Initial state: {machine.GetActiveStateNames()}");

        // Test Send
        var sendResult = machine.Send("GO");
        Console.WriteLine($"Send('GO') returned: '{sendResult}'");
        Console.WriteLine($"After Send - GetActiveStateNames(): '{machine.GetActiveStateNames()}'");

        // Reset to initial state
        machine.Send("STOP");
        Console.WriteLine($"\nReset to initial: {machine.GetActiveStateNames()}");

        // Test SendAsync
        var sendAsyncResult = await machine.SendAsync("GO");
        Console.WriteLine($"SendAsync('GO') returned: '{sendAsyncResult}'");
        Console.WriteLine($"After SendAsync - GetActiveStateNames(): '{machine.GetActiveStateNames()}'");

        // Check if they're the same
        Console.WriteLine($"\nAre they equal? {sendResult == sendAsyncResult}");
    }
}