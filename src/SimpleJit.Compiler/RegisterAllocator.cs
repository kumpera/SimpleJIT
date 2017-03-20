using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleJit.Compiler {
	
/*
arg regs RDI RSI RDX RCX R8 R9
caller saved regs RAX RCX RDX RSI RDI R8 R9 R10 - I.E. scratch regs
callee saved regs RBX R12 R13 R14 R15 RBP - I.E. calls don't clobber, but must be saved on prologue
*/
public class CallConv {
	public static readonly Register[] args = new Register[] {
		Register.RDI,
		Register.RSI,
		Register.RDX,
		Register.RCX,
		Register.R8,
		Register.R9,
	};

	public static readonly Register[] caller_saved = new Register[] {
		Register.RAX,
		Register.RCX,
		Register.RDX,
		Register.RSI,
		Register.RDI,
		Register.R8,
		Register.R9,
		Register.R10,
		//Register.R11 //Not including it for now as mono doesn't.
	};

	public static readonly Register[] callee_saved = new Register[] {
		Register.RBX,
		Register.R12,
		Register.R13,
		Register.R14,
		Register.R15,
		// Register.RBP, // we never omit the frame pointer
	};

	public static readonly Register[] ret = new Register[] {
		Register.RAX,
		Register.RDX,
	};

	public static Register RegForArg (int arg) {
		return args [arg];
	}

	public static Register ReturnReg {
		get { return Register.RAX; }
	}
}

public enum Register : byte {
	None = 0xFF,
	Dead = 0xFE,
	RAX = 0,
	RCX = 1,
	RDX = 2,
	RBX = 3,
	RSP = 4,
	RBP = 5,
	RSI = 6,
	RDI = 7,
	R8 = 8,
	R9 = 9,
	R10 = 10,
	R11 = 11,
	R12 = 12,
	R13 = 13,
	R14 = 14,
	R15 = 15,
	RIP = 16,
	RegCount = 16,
}

internal static class RegisterExtensions {
	internal static bool IsCalleeSaved (this Register reg) {
		for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
			if (CallConv.callee_saved [i] == reg)
				return true;
		}
		return false;
	}
}


public struct VarState {
	internal Register reg;
	internal int spillSlot;

	internal VarState (Register reg) {
		this.reg = reg;
		this.spillSlot = -1;
	}

	internal bool IsReg {
		get { return reg != Register.None; }
	}

	internal bool IsSpill {
		get { return spillSlot >= 0; }
	}

	internal bool IsLive {
		get {
			if (spillSlot >= 0)
				return true;
			return reg != Register.None;
		}
	}

	internal VarState (int spillSlot) {
		this.reg = Register.None;
		this.spillSlot = spillSlot;
	}

	internal bool Eq (VarState vs) {
		if (IsReg && IsSpill)
			throw new Exception ("Can't handle a var state with reg & spill");
		if (IsReg && reg == vs.reg)
			return true;
		if (IsSpill && spillSlot == vs.spillSlot)
			return true;
		return false;
	}

	public override string ToString () {
		if (reg != Register.None && spillSlot >= 0)
			return $"(VS {reg} spill[{spillSlot}])";
		if (reg != Register.None)
			return $"(VS {reg})";
		else if (spillSlot >= 0)
			return $"(VS spill[{spillSlot}])";
		else
			return "(VS UNK)";
	}
}

public class AllocRequest : IComparable<AllocRequest> {
	public int vreg;
	public VarState? preferred;
	public bool canSpill;
	public VarState result;
	bool done;

	public AllocRequest (int vreg) {
		this.vreg = vreg;
		this.result = new VarState (Register.None);
	}

	public AllocRequest (int vreg, bool canSpill, VarState? preferred) {
		this.vreg = vreg;
		this.canSpill = canSpill;
		this.preferred = preferred;
		this.result = new VarState (Register.None);
	}


	public void SetResult (VarState vs) {
		this.result = vs;
		this.done = true;
	}

	public bool Done { get{ return done; } }

	public int CompareTo (AllocRequest ar) {
		return vreg - ar.vreg;
	}

	public override string ToString () {
		if (preferred != null)
			return $"AR {vreg} CanSpill {canSpill} pref {preferred} Result {result}";
		else
			return $"AR {vreg} CanSpill {canSpill} Result {result}";
	}
}


class RegAllocState {
	VarState[] varState;
	int[] regToVar;
	BasicBlock bb;
	SortedSet<Register> callee_saved;
	bool[] spillSlots;
	int spillSlotMax = -1;
	List<Tuple<VarState, VarState>> args_repairing = new List<Tuple<VarState, VarState>> ();

	public RegAllocState (BasicBlock bb, SortedSet<Register> callee_saved) {
		varState = new VarState [bb.MaxReg ()];
		for (int i = 0; i < varState.Length; ++i) {
			varState [i].reg = Register.None;
			varState [i].spillSlot = -1;
		}

		regToVar = new int [(int)Register.RegCount];
		for (int i = 0; i < (int)Register.RegCount; ++i)
			regToVar [i] = -1;
		this.bb = bb;
		this.callee_saved = callee_saved;
	}

	//FIXME clean all this assign helpers

	Register AssignReg (int var, Register reg, HashSet<Register> inUse) {
		if (reg.IsCalleeSaved ()) {
			callee_saved.Add (reg);
			Console.WriteLine ($"\tpicked callee saved {reg}");
		} else {
			Console.WriteLine ($"\tpicked caller saved {reg}");
		}

		regToVar [(int)reg] = var;
		varState [var].reg = reg;
		if (inUse != null)
			inUse.Add (reg);
		return reg;
	}

	void AssignVS (int vreg, VarState vs) {
		varState [vreg] = vs;
		if (vs.IsReg)
			regToVar [(int)vs.reg] = vreg;
	}

	static int MaskReg (Register reg) {
		return -(1 + (int)reg);
	}

	int Conv (int vreg) {
		if (!varState [vreg].IsReg)
			throw new Exception ($"Cant conv unregistered vars! vreg {vreg} -> {varState [vreg]}");
		Register reg = varState [vreg].reg;
		if (reg == Register.None || reg == Register.Dead)
			throw new Exception ($"Can't assign bad reg to ins {reg}");
		return MaskReg (reg);
	}

	void KillVar (int vreg) {
		Console.WriteLine ($"KILL {vreg}");

		var vs = varState [vreg];
		if (vs.IsSpill)
			FreeSpillSlot (varState [vreg].spillSlot);
		if (vs.IsReg && regToVar [(int)vs.reg] == vreg)
			regToVar [(int)vs.reg] = -1;
		varState [vreg] = new VarState (Register.None);
	}

	int AllocSpillSlot (int pref) {
		if (spillSlots == null)
			spillSlots = new bool [16];

		if (pref >= 0) {
			while (spillSlots.Length <= pref)
				Array.Resize (ref spillSlots, spillSlots.Length * 2);
			if (!spillSlots [pref]) {
				spillSlots [pref] = true;
				spillSlotMax = Math.Max (spillSlotMax, pref);
				return pref;
			}
		}

		int len = spillSlots.Length;
		for (int i = 0; i < len; ++i) {
			if (spillSlots [i])
				continue;
			spillSlots [i] = true;
			spillSlotMax = Math.Max (spillSlotMax, i);
			return i;
		}
		Array.Resize (ref spillSlots, len * 2);
		spillSlots [len] = true;
		spillSlotMax = Math.Max (spillSlotMax, len);
		return len;
	}

	void FreeSpillSlot (int slot) {
		spillSlots [slot] = false;
	}

	VarState SpillRequest (AllocRequest ar) {
		Console.WriteLine ($"\tspilling {ar}");
		int slot = AllocSpillSlot (ar.preferred != null ? ar.preferred.Value.spillSlot : -1);
		ar.SetResult (new VarState (slot));

		var res = new VarState (slot);
		varState [ar.vreg] = res;
		return res;
	}

	Register FindReg (AllocRequest ar, HashSet<Register> inUse, ref Ins spillIns) {
		int vreg = ar.vreg;

		if (ar.preferred != null && ar.preferred.Value.IsReg && regToVar [(int)ar.preferred.Value.reg] == -1)
			return AssignReg (vreg, ar.preferred.Value.reg, inUse);

		var prefs = bb.RegPrefs [ar.vreg];
		foreach (var candidate in prefs.Likes) {
			if (regToVar [(int)candidate] == -1) {
				Console.WriteLine ("\t>>FindReg picked reg pref");
				return AssignReg (vreg, candidate, inUse);
			}
		}

		for (int i = 0; i < CallConv.caller_saved.Length; ++i) {
			Register candidate = CallConv.caller_saved [i];
			if (regToVar [(int)candidate] == -1)
				return AssignReg (vreg, candidate, inUse);
		}

		for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
			Register candidate = CallConv.callee_saved [i];
			if (regToVar [(int)candidate] == -1)
				return AssignReg (vreg, candidate, inUse);
		}
		Console.WriteLine ($"\tfind reg: spilling!");
		int spillVreg = -1, regularVreg = -1;

		for (int i = 0; i < (int)Register.RegCount; ++i) {
			int candVreg = regToVar [i];
			if (candVreg == -1) //ignored for some reason
				continue;
			if (inUse.Contains ((Register)i)) //can't spill active regs
				continue;
			Console.WriteLine ($"\tcandidate: {(Register)i} vreg {candVreg} vs {varState [candVreg]}");
			if (varState [candVreg].reg != (Register)i)
				throw new Exception ("WTF, invalid alloc state");
			if (varState [candVreg].spillSlot >= 0) {
				spillVreg = i; //a vreg that is already spilled is the cheapest alternative
				break;
			}
			if (regularVreg == -1 && varState [candVreg].reg != Register.None) {
				regularVreg = i;
			}	
		}

		if (spillVreg != -1) {
			var reg = varState [spillVreg].reg;
			varState [spillVreg].reg = Register.None;
			AssignReg (vreg, reg, inUse);
			Console.WriteLine ($"\tpicked spillVreg vreg {spillVreg} reg {reg}");
			return reg;
		}

		if (regularVreg != -1) {
			var reg = varState [regularVreg].reg;
			varState [regularVreg].reg = Register.None;
			varState [regularVreg].spillSlot = AllocSpillSlot (-1);
			var ins = Ins.NewFillVar (MaskReg (reg), varState [regularVreg].spillSlot);
			ins.SetNext (spillIns);
			spillIns = ins;
			AssignReg (vreg, reg, inUse);
			Console.WriteLine ($"\tpicked regularVreg vreg {regularVreg} reg {reg} got slot {varState [regularVreg].spillSlot}");
			return reg;
		}

		throw new Exception ("FindOrSpill failed");
	}

	VarState FindOrSpill (AllocRequest ar, HashSet<Register> inUse, ref Ins spillIns) {
		string s = "";
		for (int i = 0; i < varState.Length; ++i) {
			var tmp = varState [i];
			if (!tmp.IsLive)
				continue;
			if (s.Length > 0)
				s += ",";
			s += $"{i} -> {tmp}";
		}

		var iu = string.Join (",", inUse.Select (r => r.ToString ()));
		Console.WriteLine ($"FindOrSpill reg {ar.vreg} R2V ({s}) inUse ({iu})");
		if (ar.canSpill && ar.preferred != null && ar.preferred.Value.IsSpill)
			return SpillRequest (ar);

		var vs = varState [ar.vreg];
		var res = FindReg (ar, inUse, ref spillIns);

		if (vs.IsSpill) {
			Console.WriteLine ($"***need to emit spill for {ar.vreg}");

			var ins = Ins.NewSpillVar (MaskReg (res), vs.spillSlot);

			ins.SetNext (spillIns);
			spillIns = ins;

			FreeSpillSlot (vs.spillSlot);
			varState [ar.vreg].spillSlot = -1;
		}
		return new VarState (res);
	}

	void DoAlloc (SortedSet<AllocRequest> reqs, ref Ins spillIns, HashSet<Register> inUse = null) {
		int REG_MAX = CallConv.caller_saved.Length + CallConv.callee_saved.Length;

		Console.WriteLine ("alloc request:");
		foreach (var r in reqs)
			Console.WriteLine ($"\t{r}");

		if (reqs.Count > REG_MAX) {
			Console.WriteLine ("We need more regs than are available");
			//We lower in order, XXX introduce priority so we spill exit branches
			int in_use = reqs.Count;
			foreach (var ar in reqs) {
				//either can be spilled or has a spill in its preferred set
				if (ar.canSpill || (ar.preferred != null && ar.preferred.Value.spillSlot >= 0)) {
					SpillRequest (ar);
					--in_use;
				}
				if (in_use <= REG_MAX)
					break;
			}
		}

		if (inUse == null)
			inUse = new HashSet<Register> ();

		foreach (var ar in reqs) {
			if (ar.Done)
				continue;
			var res = FindOrSpill (ar, inUse, ref spillIns);
			if (!res.IsLive)
				throw new Exception ("Could neither alloc or spill, WTF");
			ar.SetResult (res);
		}

		Console.WriteLine ("alloc result:");
		foreach (var r in reqs)
			Console.WriteLine ($"\t{r}");
	}


	void SetCallInfoResult (CallInfo info) {
		info.AllocResult = new List<VarState> ();
		
		if (info.Target.InVarState == null) {
			info.Target.NeedRepairing = true;
			info.NeedRepairing = true;
			for (int i = 0; i < info.Args.Count; ++i)
				info.AllocResult.Add (varState [info.Args [i]]);
		} else {
			var repairing = new List<Tuple<VarState, VarState>> ();
			for (int i = 0; i < info.Args.Count; ++i) {
				info.AllocResult.Add (varState [info.Args [i]]);

				var targetVS = info.Target.InVarState [i];
				var thisVS = varState [info.Args [i]];
				if (targetVS.IsLive && !thisVS.Eq (targetVS))
					repairing.Add (Tuple.Create (targetVS, thisVS));
			}
			if (repairing.Count > 0)
				EmitRepairCode (info.Target, repairing);

		}
	}

	static void RepairPair (BasicBlock bb, Tuple<VarState, VarState> varPair) {
		//One var repair
		if (varPair.Item2.IsSpill) {
			if (varPair.Item1.IsSpill)
				throw new Exception ($"DONT KNOW HOW TO REPAIR mem2mem {varPair}");
			bb.Prepend (Ins.NewFillVar (MaskReg (varPair.Item1.reg), varPair.Item2.spillSlot));
		} else if (varPair.Item1.IsSpill) {
			throw new Exception ($"DONT KNOW HOW TO REPAIR with SpillVar {varPair}");

			bb.Prepend (Ins.NewSpillVar (MaskReg (varPair.Item2.reg), varPair.Item1.spillSlot));
		} else {
			bb.Prepend (Ins.NewMov (MaskReg (varPair.Item1.reg), MaskReg (varPair.Item2.reg)));
		}
	}

	static void EmitSwap (BasicBlock bb,  Tuple <VarState, VarState> varPair) {
		if (!varPair.Item1.IsReg ||!varPair.Item2.IsReg)
			throw new Exception ("Can only swap regs");

		bb.Prepend (Ins.NewSwap (MaskReg (varPair.Item1.reg), MaskReg (varPair.Item1.reg)));
	}

	static void EmitRepairCode (BasicBlock bb, List<Tuple<VarState, VarState>> repairing) {
		var table = String.Join (",", repairing.Select (t => $"{t.Item1} <= {t.Item2}"));
		Console.WriteLine ($"REPAIRING BB{bb.Number} WITH {table}");
		/*CI allocation only requires repairing when the current BB has multiple out edges
		so we always repair on the target.
		We can only repair on the target if it has a single incomming BB.
		What this mean is that we might need to remove a critical-edge at this point. TBD
		*/

		if (bb.From.Count > 1)
			throw new Exception ("Can't handle critical edges yet");


		var srcVS = new HashSet<VarState> ();
		var dstVS = new HashSet<VarState> ();

		foreach (var t in repairing) {
			dstVS.Add (t.Item1);
			srcVS.Add (t.Item2);
		}

		Console.WriteLine ("SRC: {0}", string.Join (",", srcVS));
		Console.WriteLine ("DST: {0}", string.Join (",", dstVS));
		//pick targets not in source

		while (dstVS.Count > 0) {
			Console.WriteLine ("**REPAIR ROUND");
			var tmp = new HashSet<VarState> (dstVS);
			tmp.ExceptWith (srcVS);
			if (tmp.Count == 0) {
				//Try to find swap pairs
				Console.WriteLine ("\tTrying swaps");
				for (int i = 0; i < repairing.Count; ++i) {
					var left = repairing [i];
					for (int j = i + 1; j < repairing.Count; ++j) {
						var right = repairing [j];
						if (left.Item1.Eq (right.Item2) && left.Item2.Eq (right.Item1)) {
							EmitSwap (bb, left);

							dstVS.Remove (left.Item1);
							dstVS.Remove (left.Item2);
							srcVS.Remove (left.Item1);
							srcVS.Remove (left.Item2);
							goto found_swap;
						}
					}
				}
				throw new Exception ("CAN'T DO TRIVIAL REPAIR, NEED SWAPS");
found_swap:
				continue;
			}

			foreach (var d in tmp) {
				Console.WriteLine ($"\tdoing {d}");
				dstVS.Remove (d);
				for (int i = 0; i < repairing.Count; ++i) {
					if (repairing [i].Item1.Eq (d)) {
						srcVS.Remove (repairing [i].Item2);
						RepairPair (bb, repairing [i]);
						break;
					}
				}
			}
		}

		if (dstVS.Count > 1)
			throw new Exception ("Need to figure out how to compute repair optimal swapping");

		for (int i = 0; i < bb.InVarState.Count; ++i) {
			for (int j = 0; j < repairing.Count; ++j) {
				if (bb.InVarState [i].Eq (repairing [j].Item1)) {
					bb.InVarState [i] = repairing [j].Item2;
					break;
				}
			}
		}
	}

	void CallInfo (CallInfo info, SortedSet<AllocRequest> reqs) {
		if (info.Target.InVarState == null) { //This happens the loop tail-> loop head edge.
			for (int i = 0; i < info.Args.Count; ++i) {
				reqs.Add (new AllocRequest (info.Args [i], true, null));
			}
		} else {
			for (int i = 0; i < info.Args.Count; ++i) {
				var vs = info.Target.InVarState [i];
				reqs.Add (new AllocRequest (info.Args [i], true, vs.IsLive ? vs : (VarState?)null));
			}
		}
	}

	void RepairInfo (BasicBlock bb, BasicBlock from, CallInfo info) {
		Console.WriteLine ($"REPAIRING THE LINK BB{from.Number} to BB{bb.Number}");

		info.NeedRepairing = false;
		var repairing = new List<Tuple<VarState, VarState>> ();
		for (int i = 0; i < info.Args.Count; ++i) {
			var source = info.AllocResult [i];

			if (bb.InVarState [i].IsLive && !bb.InVarState [i].Eq (source))
				repairing.Add (Tuple.Create (bb.InVarState [i], source));
		}
		if (repairing.Count > 0)
			EmitRepairCode (bb, repairing);
	}

	public void Def (Ins ins, int vreg) {
		//This just kills the reg
		var vs = varState [vreg];
		if (!vs.IsLive) {
			ins.MakeNop ();
		} else if (vs.IsReg) {
			ins.Dest = Conv (vreg);
			regToVar [(int)vs.reg] = -1;
		} else {
			ins.Op = Ops.SpillConst; //FIXME this needs an op-to-spillop conv
			ins.Const1 = vs.spillSlot;
		}

		KillVar (vreg);
	}


	public void Move (Ins ins, int to, int from) {
		var vsTo = varState [to];
		var vsFrom = varState [from];
		//this is a dead store, reduce the live range of from
		if (!vsTo.IsLive) {
			if (!vsFrom.IsLive) {
				SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
				reqs.Add (new AllocRequest (from));
				Ins spillIns = null;
				DoAlloc (reqs, ref spillIns);
				if (spillIns != null)
					ins.Append (spillIns);
			}

			ins.MakeNop ();
			return;
		}

		//If this is the last usage of from, we can treat this as a rename
		if (!vsFrom.IsLive) {
			Console.WriteLine ($"RENAMING {from} to {to}");
			KillVar (to);
			varState [from] = vsTo;
			if (vsTo.IsReg)
				regToVar [(int)vsTo.reg] = from;
			if (vsTo.IsSpill)
				spillSlots [vsTo.spillSlot] = true;
			ins.MakeNop ();
		} else {
			if (vsFrom.IsReg && vsTo.IsReg) {
				ins.Dest = Conv (to);
				ins.R0 = Conv (from);
			} else {
				if (vsFrom.IsSpill && vsTo.IsSpill)
					throw new Exception ("IMPLEMENT ME: mem2mem mov");
				if (vsTo.IsSpill)
					throw new Exception ($"IMPLEMENT ME: spilled mov {vsFrom.IsSpill} {vsTo.IsSpill}");
				if (vsFrom.IsSpill && !vsFrom.IsReg) {
					ins.ReplaceWith (Ins.NewFillVar (Conv (to), vsFrom.spillSlot));
				} else {
					throw new Exception ("NRIEjid");
				}
			}
			KillVar (to);
		}
	}

	bool ShouldSwap (Ins ins) {
		if (ins.Op != Ops.Add) //or mul or reverse subtract on ARM
			return false;

		var vsR0 = varState [ins.R0];
		var vsR1 = varState [ins.R1];
		var vsDest = varState [ins.Dest];

		//on x86, it's profitable as R0 is clobbered and we can simply extend dest to r0
		if (vsR0.IsLive && !vsR1.IsLive) {
			Console.WriteLine ("r0 live r1 dead");
			return true;
		}

		if (!vsR0.IsLive && !vsR1.IsLive && vsDest.IsReg && bb.RegPrefs [ins.R1].DoesLike (vsDest.reg)) {
			Console.WriteLine ("r1 likes dest");
			return true;
		}

		return false;
	}

	public void BinOp (Ins ins, int dest, int r0, int r1) {
		if (ShouldSwap (ins)) {
			Console.WriteLine ($"SWAPPING args for {ins}");
			ins.R0 = r1;
			ins.R1 = r0;
			r1 = ins.R1;
			r0 = ins.R0;
		}

		var vsDest = varState [dest];
		var vsR0 = varState [r0];
		var vsR1 = varState [r1];

		var inUse = new HashSet<Register> ();
		SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();

		//something must use $dest
		if (!vsDest.IsLive)
			throw new Exception ($"Dead var or bug? dest: {dest} {vsDest}");

		//if $dest is spilled, we need to generate a spill 
		if (vsDest.IsSpill)
			reqs.Add (new AllocRequest (dest));
		else
			inUse.Add (vsDest.reg);

		//Binop follows x86 rules of r0 getting clobbered.
		if (vsR0.IsLive && vsR1.IsLive) {
			
		}
		//if vsR0 is not live, we assign it to whatever dest gets
		if (vsR0.IsLive && vsR0.IsReg)
			inUse.Add (vsR0.reg);

		if (!vsR1.IsLive)
			reqs.Add (new AllocRequest (r1));
		else if (vsR1.IsReg)
			inUse.Add (vsR1.reg);

		Ins spillIns = null;
		DoAlloc (reqs, ref spillIns, inUse);
		if (spillIns != null)
			ins.Append (spillIns);

		if (!vsR0.IsLive)
			AssignVS (r0, varState [dest]);
		else {
			ins.Prepend (Ins.NewMov (MaskReg (varState [dest].reg), MaskReg (vsR0.reg)));
		}

		ins.Dest = Conv (dest);
		ins.R0 = Conv (dest);
		ins.R1 = Conv (r1);

		KillVar (dest);
	}

	public void UnOp (Ins ins, int dest, int r0) {
		var vsDest = varState [dest];
		var vsR0 = varState [r0];

		var inUse = new HashSet<Register> ();
		SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();

		if (!vsDest.IsLive)
			throw new Exception ($"Dead var or bug? dest: {vsDest}");

		if (vsDest.IsSpill)
			reqs.Add (new AllocRequest (dest));
		else
			inUse.Add (vsDest.reg);

		if (vsR0.IsLive && vsR0.IsReg)
			inUse.Add (vsR0.reg);

		Ins spillIns = null;
		DoAlloc (reqs, ref spillIns, inUse);
		if (spillIns != null)
			ins.Append (spillIns);

		if (!vsR0.IsLive)
			AssignVS (r0, varState [dest]);
		else {
			ins.Prepend (Ins.NewMov (MaskReg (varState [dest].reg), MaskReg (vsR0.reg)));
		}

		ins.Dest = Conv (dest);
		ins.R0 = Conv (dest);

		KillVar (dest);
	}

	public void CmpI (Ins ins, int r0) {
		var vsR0 = varState [r0];

		SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();

		if (!vsR0.IsLive)
			reqs.Add (new AllocRequest (r0));

		Ins spillIns = null;
		DoAlloc (reqs, ref spillIns);
		if (spillIns != null)
			ins.Append (spillIns);

		ins.R0 = Conv (r0);
	}

	public void Cmp (Ins ins, int r0, int r1) {
		var vsR0 = varState [r0];
		var vsR1 = varState [r1];

		SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
		var inUse = new HashSet<Register> ();

		if (vsR0.IsLive && vsR0.IsReg)
			inUse.Add (vsR0.reg);
		else
			reqs.Add (new AllocRequest (r0));

		if (vsR1.IsLive && vsR1.IsReg)
			inUse.Add (vsR1.reg);
		else
			reqs.Add (new AllocRequest (r1));

		Ins spillIns = null;
		DoAlloc (reqs, ref spillIns);
		if (spillIns != null)
			ins.Append (spillIns);

		ins.R0 = Conv (r0);
		ins.R1 = Conv (r1);
	}

	public void CondBranch (Ins ins, CallInfo[] infos) {
		SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
		for (int j = 0; j < infos.Length; ++j)
			this.CallInfo (infos [j], reqs);

		Ins spillIns = null;
		DoAlloc (reqs, ref spillIns);
		if (spillIns != null)
			throw new Exception ("CondBranch can't handle spills");

		for (int j = 0; j < infos.Length; ++j)
			this.SetCallInfoResult (infos [j]);
	}

	public void DirectBranch (Ins ins, CallInfo[] infos) {
		SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
		for (int j = 0; j < infos.Length; ++j)
			this.CallInfo (infos [j], reqs);

		Ins spillIns = null;
		DoAlloc (reqs, ref spillIns);
		if (spillIns != null)
			throw new Exception ("DirectBranch can't handle spills");

		for (int j = 0; j < infos.Length; ++j)
			this.SetCallInfoResult (infos [j]);
	}

	public void SetRet (Ins ins, int vreg) {
		if (varState [vreg].IsLive)
			throw new Exception ("SetReg MUST be the last use of the vreg on its BB");

		Register reg = CallConv.ReturnReg;
		if (regToVar [(int)reg] >= 0)
			throw new Exception ("For some reason, someone is already using SetReg's register. Fix your IR!");

		AssignReg (vreg, reg, null);

		ins.R0 = Conv (vreg);
	}

	public void LoadArg (Ins ins, int dest, int position) {
		Register reg = CallConv.RegForArg (position);
		var vs = varState [dest];

		if (!vs.IsReg || vs.reg != reg) {
			Console.WriteLine ($"Need to fixup incoming regs. I want {reg} but have {vs}");
			args_repairing.Add (Tuple.Create (vs, new VarState (reg)));
		}
		ins.MakeNop ();
	}

	Ins SpillAroundCalls (int vreg)
	{
		var oldReg = varState [vreg].reg;

		if (!varState [vreg].IsLive || !varState [vreg].IsReg)
			throw new Exception ($"Invalid call spill for {vreg}");

		regToVar [(int)oldReg] = -1;

		for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
			if (regToVar [(int)CallConv.callee_saved [i]] == -1) {
				var reg = CallConv.callee_saved [i];
				AssignReg (vreg, reg, null);

				return Ins.NewMov (MaskReg (oldReg), MaskReg (reg));
			}
		}

		//no callee saves regs available :(
		varState [vreg].reg = Register.None;
		varState [vreg].spillSlot = AllocSpillSlot (-1);
		return Ins.NewFillVar (MaskReg (oldReg), varState [vreg].spillSlot);
	}

	public void Call (Ins ins, int dest, int[] argVars) {
		var inUse = new HashSet<Register> ();

		//handle return value
		Ins postSpill = null;
		Register retReg = CallConv.ret [0];
		if (regToVar [(int)retReg] == dest) { //we got lucky, already in place
			// inUse.Add (retReg);
		} else if (regToVar [(int)retReg] == -1) {
			throw new Exception ($"need to emit copy to {retReg}");
		} else {
			var vs = varState [dest];
			int vreg = regToVar [(int)retReg];
			Console.WriteLine ($"need to spill {vreg} as it's used a return reg");
			
			if (vs.IsReg) {
				postSpill = Ins.NewMov (MaskReg (vs.reg), MaskReg (retReg));
				regToVar [(int)vs.reg] = -1; //XXX assign reg should do this?
			} else {
				throw new Exception ("Need to handle return to spill");
			}

			postSpill.SetNext (SpillAroundCalls (vreg));	

			AssignReg (dest, retReg, null);

			// throw new Exception ($"need to deal with ret val {regToVar [(int)retReg]} -> {varState [regToVar [(int)retReg]]} and I need {retReg}");
		}
		ins.Dest = Conv (dest);

		for (int i = 0; i < CallConv.caller_saved.Length; ++i) {
			var reg = CallConv.caller_saved [i];
			int vreg = regToVar [(int)reg];
			if (vreg == -1)
				continue;
			if (reg == retReg)
				continue;

			Console.WriteLine ($"Need to spill R{vreg} =? {reg}");
			if (postSpill == null)
				postSpill = SpillAroundCalls (vreg);
			else
				postSpill.Append (SpillAroundCalls (vreg));
		}

		if (postSpill != null)
			ins.Append (postSpill);
		inUse.Add (retReg);

		for (int i = 0; i < argVars.Length; ++i) {
			var vs = varState [argVars [i]];
			if (vs.IsLive)
				throw new Exception ($"ARG {i} is live!");
			Register aReg = CallConv.args [i];
			if (regToVar [(int)aReg] != -1)
				throw new Exception ($"Call reg not available {aReg}!");

			AssignReg (argVars [i], aReg, null);
			argVars [i] = Conv (argVars [i]);
			inUse.Add (aReg);
		}

		KillVar (dest);
		// throw new Exception ($"all good! {ins}");
	}

	public int Finish () {
		bb.InVarState = new List<VarState> ();
		for (int i = 0; i < bb.InVars.Count; ++i) {
			var vs = varState [i];
			// if (!vs.IsLive)
			// 	throw new Exception ($"Bad REGALLOC didn't allocate vreg {i}!");
			bb.InVarState.Add (vs);
		}

		if (args_repairing.Count > 0) {
			EmitRepairCode (bb, args_repairing);
		}

		if (bb.NeedRepairing) {
			Console.WriteLine ("DOING BB REPAIR ON FINISH BB{0}", bb.Number);
			bb.NeedRepairing = false;
			for (int i = 0; i < bb.From.Count; ++i) {
				CallInfo[] infos = bb.From [i].LastIns.CallInfos;
				for (int j = 0; j < infos.Length; ++j) {
					if (infos [j].Target == bb && infos [j].NeedRepairing) {
						RepairInfo (bb, bb.From [i], infos [j]);
						break;
					}
				}
			}
		}
		//Returns the number of spill slots used. spillSlotMax 0 means we used 1 slot
		return spillSlotMax + 1;
	}

	public string State {
		get {
			string s = "";
			for (int i = 0; i < varState.Length; ++i) {
				var vs = varState [i];
				if (!vs.IsLive)
					continue;
				if (s.Length > 0)
					s += ",";
				s += $"{i} -> {vs}";
			}
			string ss = "";
			if (spillSlots != null) {
				for (int i = 0; i < spillSlots.Length; ++i) {
					if (!spillSlots [i])
						continue;
					if (ss.Length > 0)
						ss += ",";
					ss += $"{i}";
				}
			}
			return $"RA! ({s}) SS ({ss})";
		}
	}
}

internal struct RegPrefs {
	internal int vreg;
	HashSet<Register> likes;

	internal bool DoesLike (Register reg) {
		return likes != null && likes.Contains (reg);
	}

	internal void AddLike (Register reg) {
		if (likes == null)
			likes = new HashSet <Register> ();
		likes.Add (reg);
	}

	internal void LikesSameAs (ref RegPrefs other) {
		if (other.likes	== null)
			return;
		if (likes == null)
			likes = new HashSet <Register> ();

		likes.UnionWith (other.likes);
	}

	internal void MergeArgs (ref RegPrefs other) {
		if (other.likes	== null || other.likes.Count == 0)
			return;

		if (likes == null)
			likes = new HashSet <Register> ();

		if (likes.Count == 0)
			likes.UnionWith (other.likes);
		else
			likes.IntersectWith (other.likes);
	}

	internal HashSet<Register> Likes {
		get {
			if (likes == null)
				return new HashSet <Register> ();
			return likes;
		}
	}

	public override string ToString () {
		if (likes == null)
			return "";
		var regs = string.Join (",", likes.Select (r => "+" + r.ToString ()));
		return $"R{vreg} => {regs})";
	}
}

public class RegPreferencesPass {
	Compiler cc;

	public RegPreferencesPass (Compiler cc) {
		this.cc = cc;
	}

	RegPrefs[] InitBB (BasicBlock bb) {
		if (bb.RegPrefs != null)
			return bb.RegPrefs;
		RegPrefs[] prefs = new RegPrefs [bb.MaxReg ()];
		for (int i = 0; i < prefs.Length; ++i)
			prefs [i].vreg = i;
		bb.RegPrefs = prefs;
		return prefs;
	}

	void ForwardToBB (BasicBlock from, CallInfo ci) {
		var fromPrefs = InitBB (from);

		var to = ci.Target;
		var toPrefs = InitBB (to);

		for (int i = 0; i < ci.Args.Count; ++i) {
			int vreg = ci.Args [i];
			toPrefs [i].MergeArgs (ref fromPrefs [vreg]);
		}
	}

	static string PrefsToString (RegPrefs[] prefs) {
		var r = "";
		for (int i = 0; i < prefs.Length; ++i) {
			var str = prefs [i].ToString ();
			if (str != "") {
				if (r != "")
					r += ",";
				r += str;
			}
		}
		return $"({r})";
	}

	void ForwardPrefs (BasicBlock bb) {
		RegPrefs[] prefs = InitBB (bb);

		Console.WriteLine ($">Forward prefs of BB{bb.Number}: INITIAL {PrefsToString (prefs)}");

		for (Ins ins = bb.FirstIns; ins != null; ins = ins.Next) {
			switch (ins.Op) {
			case Ops.LoadArg:
				prefs [ins.Dest].AddLike (CallConv.args [ins.Const0]);
				break;
			//XXX we'd like if R0 could share reg with dreg, but we can only do this on a backward pass if this is the last use of R0
			case Ops.Add:
			case Ops.AddI:
				// prefs [ins.Dest].LikesSameAs (ref prefs [ins.R0]);
				break;
			case Ops.Cmp: //this has no constraints
			case Ops.CmpI: //this has no constraints
			case Ops.IConst:
			case Ops.SetRet: //this is a backwards constraint
				break;
			case Ops.Ble:
			case Ops.Blt:
			case Ops.Bg:
			case Ops.Bne:
			case Ops.Beq:
				ForwardToBB (bb, ins.CallInfos [0]);
				ForwardToBB (bb, ins.CallInfos [1]);
				break;
			case Ops.Br: //we handle BB args separatedly
				ForwardToBB (bb, ins.CallInfos [0]);
				break;
			//XXX nothing much we can do for arg regs, it matters more if they outlive this call
			case Ops.Call:
				prefs [ins.Dest].AddLike (CallConv.ret [0]);
				break;
			default:
				throw new Exception ($"Can't handle {ins}");
			}
		}
		Console.WriteLine ($"PREFS: {PrefsToString (prefs)} for BB{bb.Number}");
	}


	public void Run () {
		Console.WriteLine ("Computing register preferences");

		BasicBlock bb;
		var q = new List <BasicBlock> (); //XXX use a queue

		//first we do a forward pass to propagate returns and in arg regs
		for (bb = cc.first_bb; bb != null; bb = bb.NextInOrder)
			bb.Enqueued = bb.Done = false;

		q.Add (cc.first_bb);

		while (q.Count > 0) {
			var current = q [0];
			q.RemoveAt (0);
			current.Enqueued = false;
			current.Done = true;

			ForwardPrefs (current);

			foreach (var next in current.To) {
				if (!next.Enqueued && !next.Done) {
					q.Add (next);
					next.Enqueued = true;
				}
			}
		}
	}
}
}
