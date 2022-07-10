// using System.Collections;

// namespace AgentsSystem;
// public interface IBirth<T> where T : IAgent
// {
//     void Create(T parent);
// }

// public readonly struct BirthWrapper<T> where T : IAgent
// {
//     readonly IBirth<T> Action;
//     readonly IEnumerable<int>? Targets;
//     readonly int Quantity;

//     public BirthWrapper(IBirth<T> birthAction)
//     {
//         Action = birthAction;
//         Targets = null;
//         Quantity = 0;
//     }
//     public BirthWrapper(IBirth<T> birthAction, int target)
//     {
//         Action = birthAction;
//         Targets = new int[]{ target };
//         Quantity = 0;
//     }
//     public BirthWrapper(int count, IBirth<T> birthAction)
//     {
//         Action = birthAction;
//         Targets = null;
//         Quantity = count;
//     }
//     public BirthWrapper(IBirth<T> birthAction, IEnumerable<int> targets)
//     {
//         Action = birthAction;
//         Targets = targets;
//         Quantity = 0;
//     }

//     public void Process(T parent)
//     {
//         if (Targets == null)
//         {
//             if (Quantity <= 1)
//                 Action.Create(parent);
//             else
//                 for(int i = 0; i < Quantity; ++i)
//                     Action.Create(parent);
//         }
//         else
//             foreach(var target in Targets)
//                 Action.Create(agents[recipient]);
//     }
// }