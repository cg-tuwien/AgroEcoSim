using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AgentsSystem;

public enum Transaction { Unknown = 0, Increase = 1, Decrease = 2}
public interface IMessage<T> where T : struct, IAgent
{
    void Receive(ref T agent, uint timestep);
    Transaction Type { get; }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct MessageWrapper<T> where T : struct, IAgent
{
    readonly IMessage<T> Message;
    readonly IEnumerable<int>? Recipients;
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

	#if HISTORY_LOG
    //TODO make readonly struct records for net6.0
	[StructLayout(LayoutKind.Auto)] public readonly struct SimpleMsgLog
	{
		public readonly uint TimeStep;
		public readonly ulong MsgID;
		public readonly ulong RecipientID;
		public readonly float Amount;
        public SimpleMsgLog(uint timestep, ulong msgID, ulong recipientID, float amount)
        {
            TimeStep = timestep;
            MsgID = msgID;
            RecipientID = recipientID;
            Amount = amount;
        }
	}

    //TODO make readonly struct records for net6.0
	[StructLayout(LayoutKind.Auto)] public readonly struct PullMsgLog
	{
		public readonly uint TimeStep;
		public readonly ulong MsgID;
		public readonly ulong SourceID;
		public readonly ulong TargetID;
		public readonly float Amount;

        public PullMsgLog(uint timestep, ulong msgID, ulong sourceID, ulong targetID, float amount)
        {
            TimeStep = timestep;
            MsgID = msgID;
            SourceID = sourceID;
            TargetID = targetID;
            Amount = amount;
        }
	}
	#endif