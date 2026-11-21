using System;

/// <summary>Host-owned menu choices; never authorizes a camera input or combat action.</summary>
public sealed class RedboolPvpSelection
{
    public Guid Run { get; private set; }
    public readonly int[] Cursor = { 0, 5 };
    public readonly bool[] Locked = new bool[2];
    public readonly uint[] Revision = new uint[2];
    public void Begin()
    {
        Run=Guid.NewGuid();Locked[0]=Locked[1]=false;Revision[0]=Revision[1]=0;
    }
    public void ParticipantChanged(int slot)
    {
        if(slot<0||slot>1)return;
        Locked[slot]=false;
        // Invalidate an in-flight confirm from the previous person, even if
        // the visible cursor and lock were already unchanged.
        if(Revision[slot]<uint.MaxValue)Revision[slot]++;
        else Begin();
    }
    public bool Apply(int slot,Guid run,uint revision,int character,bool confirmed,int rosterCount)
    {
        if(slot<0||slot>1||run==Guid.Empty||run!=Run||revision!=Revision[slot]
            ||character<0||character>=rosterCount||Revision[slot]==uint.MaxValue)return false;
        if((Locked[slot]||confirmed)&&character!=Cursor[slot])return false;
        if(Cursor[slot]==character&&Locked[slot]==confirmed)return true;
        Cursor[slot]=character;Locked[slot]=confirmed;Revision[slot]++;
        return true;
    }
    public void Remote(Guid run,uint first,uint second,int cursor0,int cursor1,bool locked0,bool locked1)
    {
        Run=run;Revision[0]=first;Revision[1]=second;
        Cursor[0]=cursor0;Cursor[1]=cursor1;Locked[0]=locked0;Locked[1]=locked1;
    }
}
