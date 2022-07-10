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
            if (src.Count > Environment.ProcessorCount)
                Parallel.ForEach(src, msg => msg.Process(agents));
            else
                foreach(var msg in src) msg.Process(agents);
            src.Clear();
            src = WriteTMP ? BufferTMP : Buffer;
        }
    }    
}