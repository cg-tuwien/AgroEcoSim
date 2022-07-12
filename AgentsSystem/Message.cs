using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AgentsSystem;
public interface IMessage<T> where T : struct, IAgent
{
    void Receive(ref T agent);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct MessageWrapper<T> where T : struct, IAgent
{
    readonly IMessage<T> Message;
    readonly IEnumerable<int>? Recipients;

    public MessageWrapper(IMessage<T> msg)
    {
        Message = msg;
        Recipients = null;
    }
    public MessageWrapper(IMessage<T> msg, int recipient)
    {
        Message = msg;
        Recipients = new int[]{ recipient };
    }

    public MessageWrapper(IMessage<T> msg, IEnumerable<int> recipients)
    {
        Message = msg;
        Recipients = recipients;
    }

    public void Process(T[] agents)
    {
        if (Recipients == null)
            for(int i = 0; i < agents.Length; ++i)
                //agents[i] = Message.Receive(agents[i]);
                Message.Receive(ref agents[i]);
        else
            foreach(var recipient in Recipients)
                //agents[recipient] = Message.Receive(agents[recipient]);
                Message.Receive(ref agents[recipient]);
    }
}