using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentsSystem;

public class PostBox<T> where T : struct, IAgent
{ 
    readonly List<MessageWrapper<T>> Buffer = new();
    readonly List<MessageWrapper<T>> BufferTMP = new();
    bool WriteTMP = false;

    public void Add(MessageWrapper<T> msg)
    {
        if (WriteTMP)
            lock(BufferTMP) BufferTMP.Add(msg);
        else
            lock(Buffer) Buffer.Add(msg);
    }

    //There MUST NOT be a non-array Process method. Otherwise refs will not work
    public void Process(T[] agents)
    {
        var src = WriteTMP ? BufferTMP : Buffer;
        while (src.Count > 0)
        {
            WriteTMP = !WriteTMP;
#if !DEBUG
            if (src.Count > 2 * Environment.ProcessorCount)
            {
                //Parallel.ForEach(src, msg => msg.Process(agents));

                //Handle all increases first to create enough buffer for later decreasing
                Parallel.ForEach(src, msg => { if (msg.Type == Transaction.Increase) msg.Process(agents); });
                //only then handle decreases
                Parallel.ForEach(src, msg => { if (msg.Type != Transaction.Increase) msg.Process(agents); });
            }
            else
#endif
            {
                //Handle all increases first to create enough buffer for later decreasing
                foreach(var msg in src)
                    if (msg.Type == Transaction.Increase)
                        msg.Process(agents);
                //only then handle decreases
                foreach(var msg in src)
                    if (msg.Type != Transaction.Increase)
                        msg.Process(agents);
            }
            src.Clear();
            src = WriteTMP ? BufferTMP : Buffer;
        }
    }    
}