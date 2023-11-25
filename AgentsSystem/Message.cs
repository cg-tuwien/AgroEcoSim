using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AgentsSystem;

public enum Transaction : byte { Unknown = 0, Increase = 1, Decrease = 2}
public interface IMessage<T> where T : struct, IAgent
{
    void Receive(ref T agent, uint timestep);
    bool Valid { get; }
    Transaction Type { get; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct MessageWrapper<T> where T : struct, IAgent
{
    readonly IMessage<T> Message;
    readonly IEnumerable<int>? Recipients;
    public bool Valid => Message.Valid;
    public Transaction Type => Message.Type;

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

    public void Process(T[] agents, uint timestep)
    {
        if (Recipients == null)
        {
            for(int i = 0; i < agents.Length; ++i)
                //agents[i] = Message.Receive(agents[i]);
                Message.Receive(ref agents[i], timestep);
        }
        else
            foreach(var recipient in Recipients)
                //agents[recipient] = Message.Receive(agents[recipient]);
                Message.Receive(ref agents[recipient], timestep);
    }
}