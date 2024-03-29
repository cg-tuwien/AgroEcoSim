using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentsSystem;

public class PostBox<T> where T : struct, IAgent
{
    readonly List<MessageWrapper<T>> Buffer = new();
    readonly List<MessageWrapper<T>> BufferTMP = new();
    bool WriteTMP = false;

    public bool AnyMessages => (WriteTMP ? BufferTMP.Count : Buffer.Count) > 0;
    public void Add(MessageWrapper<T> msg)
    {
        if (msg.Valid)
        {
            if (WriteTMP)
                lock(BufferTMP) BufferTMP.Add(msg);
            else
                lock(Buffer) Buffer.Add(msg);
        }
    }

    public void Clear()
    {
        var src = WriteTMP ? BufferTMP : Buffer;
        lock(src) src.Clear();
    }

    //There MUST NOT be a non-array Process method. Otherwise refs will not work
    public void Process(uint timestep, T[] agents)
    {
        var src = WriteTMP ? BufferTMP : Buffer;
        while (src.Count > 0)
        {
            WriteTMP = !WriteTMP;
            //Handle all increases first to create enough buffer for later decreasing
            lock(src)
            {
                foreach(var msg in src)
                    if (msg.Type == Transaction.Increase)
                        msg.Process(agents, timestep);
                //only then handle decreases
                foreach(var msg in src)
                    if (msg.Type != Transaction.Increase)
                        msg.Process(agents, timestep);
                src.Clear();
            }
            src = WriteTMP ? BufferTMP : Buffer;
        }
    }
}