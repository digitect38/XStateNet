#r "C:\Develop25\XStateNet\XStateNet5Impl\bin\Debug\net8.0\XStateNet.dll"

using XStateNet;
using System;
using System.Threading.Tasks;

var json = @"{
    'id': 'afterTest',
    'initial': 'a',
    'states': {
        'a': {
            'after': {
                '100': 'b'
            }
        },
        'b': {}
    }
}";

var machine = StateMachine.CreateFromScript(json);
machine.Start();

Console.WriteLine($"Initial state: {machine.GetActiveStateString()}");
await Task.Delay(150);
Console.WriteLine($"After 150ms: {machine.GetActiveStateString()}");
