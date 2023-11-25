using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace AgentsSystem;

[StructLayout(LayoutKind.Auto)]
public readonly struct TransactionStruct
{
    public readonly float Amount;
    public readonly int SrcIndex;
    public readonly int DstIndex;

    public TransactionStruct(int srcIndex, int dstIndex, float amount)
    {
        Amount = amount;
        SrcIndex = srcIndex;
        DstIndex = dstIndex;
    }
}

public readonly struct TransactionsBox
{
    public readonly List<List<TransactionStruct>> Buffer;

    public TransactionsBox() => Buffer = new();

    public bool AnyTransactions => Buffer.Any(x => x.Count > 0);

    public void Add(int srcIndex, int dstIndex, byte substance, float amount)
    {
        if (substance >= Buffer.Count)
            for(int i = Buffer.Count; i <= substance; ++i)
                Buffer.Add(new());

        Buffer[substance].Add(new(srcIndex, dstIndex, amount));
    }

    public void Clear()
    {
        foreach(var item in Buffer)
            item.Clear();
    }
}