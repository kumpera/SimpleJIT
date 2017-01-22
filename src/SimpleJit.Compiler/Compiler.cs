//
// Compiler.cs
//
// Author:
//   Rodrigo Kumpera  <kumpera@gmail.com>
//
//
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using System.IO;
using SimpleJit.CIL;
using SimpleJit.Metadata;
using System.Linq;

/* Allready found design flaws:

The use of LoadArg sucks as it is the same reg shuffling problem of repairing and it doesn't allow for a global decision to be made.
	-If we support external allocation of BB::InVars (as a way to handle LCOV), this becomes a common case


TODO:
	implement cprop, dce and isel as part of the front-end
	spilling //basic done, lots of corner cases left TBH
	calls
	2 pass alloc (forward pass for prefs, backward pass for alloc)
	n-var repairing
	critical edges
	valuetypes
	byref
	floating point
	more ops
	let the regalloc change encoding (add reg, reg -> add reg, [spill slot])

*/
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
			// Register.RDX,
			// Register.RSI,
			// Register.RDI,
			// Register.R8,
			// Register.R9,
			// Register.R10,
			//Register.R11 //Not including it for now as mono doesn't.
		};

		public static readonly Register[] callee_saved = new Register[] {
			// Register.RBX,
			// Register.R12,
			// Register.R13,
			// Register.R14,
			// Register.R15,
			// Register.RBP, // we never omit the frame pointer
		};

		public static Register RegForArg (int arg) {
			return args [arg];
		}

		public static Register ReturnReg {
			get { return Register.RAX; }
		}
	}

	internal static class IntExtensions {
		internal static Register UnmaskReg (this int v) {
			return (Register)(-v - 1);
		}

		internal static string V2S (this int vreg) {
			if (vreg >= 0)
				return "R" + vreg;
			return UnmaskReg (vreg).ToString ();
		}
		
	}

	internal static class RegisterExtensions {
		internal static bool Valid (this Register reg) {
			return reg != Register.None && reg != Register.Dead;
		}

		internal static bool IsCalleeSaved (this Register reg) {
			for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
				if (CallConv.callee_saved [i] == reg)
					return true;
			}
			return false;
		}
	}

	public enum Ops {
		IConst,
		Mov,
		Add,
		Ble,
		Blt,
		Br,
		//Pseudo ops used by reg alloc
		LoadArg,
		SpillVar,
		SpillConst,
		FillVar,
		SetRet,
		Nop
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
			if (reg != Register.None)
				return $"(VS {reg})";
			else if (spillSlot >= 0)
				return $"(VS spill[{spillSlot}])";
			else
				return "(VS UNK)";
		}
	}

	class RegAllocState {
		VarState[] varState;
		int[] regToVar;
		BasicBlock bb;
		SortedSet<Register> callee_saved;

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

		void Assign (int var, Register reg) {
			regToVar [(int)reg] = var;
			varState [var].reg = reg;
		}

		VarState Assign2 (int var, Register reg, HashSet<Register> inUse) {
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
			return new VarState (reg);
		}

		void Assign3 (int vreg, VarState vs) {
			varState [vreg] = vs;
			if (vs.IsReg)
				regToVar [(int)vs.reg] = vreg;
		}

		Register FindReg () {
			var s = string.Join (",", regToVar.Where (reg => reg != -1).Select ((reg,idx) => $"{(Register)idx} -> {reg}"));
			for (int i = 0; i < CallConv.caller_saved.Length; ++i) {
				Register candidate = CallConv.caller_saved [i];
				if (regToVar [(int)candidate] == -1) {
					Console.WriteLine ($"find reg: got {candidate}: prev state ({s})");
					return candidate;
				}
			}

			for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
				Register candidate = CallConv.callee_saved [i];
				if (regToVar [(int)candidate] == -1) {
					callee_saved.Add (candidate);
					Console.WriteLine ($"find reg: got {candidate}: prev state ({s})");
					return candidate;
				}
			}
			Console.WriteLine ($"find reg: got NONE: prev state ({s})");

			return Register.None;
		}

		Register Alloc (int vreg) {
			Register reg = FindReg ();
			if (reg == Register.None)
				throw new Exception ("Need to spill");
			Assign (vreg, reg);
			return reg;
		}

		static int MaskReg (Register reg) {
			return -(1 + (int)reg);
		}

		int Conv (int vreg) {
			Register reg = varState [vreg].reg;
			if (reg == Register.None || reg == Register.Dead)
				throw new Exception ($"Can't assign bad reg to ins {reg}");
			return MaskReg (reg);
		}

		int Conv2 (int vreg) {
			if (!varState [vreg].IsReg)
				throw new Exception ("Cant conv unregistered vars!");
			Register reg = varState [vreg].reg;
			if (reg == Register.None || reg == Register.Dead)
				throw new Exception ($"Can't assign bad reg to ins {reg}");
			return MaskReg (reg);
		}

		void KillVar2 (int vreg) {
			if (varState [vreg].IsSpill)
				FreeSpillSlot (varState [vreg].spillSlot);
			varState [vreg] = new VarState (Register.None);
		}
		
		public void Def (Ins ins, int vreg) {
			//This just kills the reg
			var vs = varState [vreg];
			if (vs.IsReg) {
				ins.Dest = Conv2 (vreg);				
				regToVar [(int)vs.reg] = -1;
			} else {
				// throw new Exception ("DUNNO THIS");
				ins.Op = Ops.SpillConst;
				ins.Const1 = vs.spillSlot;
			}

			KillVar2 (vreg);
		}


		public void Move (Ins ins, int to, int from) {
			var vsTo = varState [to];
			var vsFrom = varState [from];
			//this is a dead store, reduce the live range of from
			if (!vsTo.IsLive) {
				if (!vsFrom.IsLive) {
					SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
					reqs.Add (new AllocRequest (from));
					DoAlloc (reqs);
				}

				ins.Op = Ops.Nop;
				return;
			}

			//If this is the last usage of from, we can treat this as a rename
			if (!vsFrom.IsLive) {
				Console.WriteLine ($"RENAMING {from} to {to}");
				varState [from] = vsTo;
				if (vsTo.IsReg)
					regToVar [(int)vsTo.reg] = from;
				ins.Op = Ops.Nop;
			} else {
				if (vsFrom.IsReg && vsTo.IsReg) {
					ins.Dest = Conv2 (to);
					ins.R0 = Conv2 (from);
				} else {
					if (vsFrom.IsSpill && vsTo.IsSpill)
						throw new Exception ("IMPLEMENT ME: mem2mem mov");
					if (vsTo.IsSpill)
						throw new Exception ($"IMPLEMENT ME: spilled mov {vsFrom.IsSpill} {vsTo.IsSpill}");
					if (vsFrom.IsSpill && !vsFrom.IsReg) {
						ins.Op = Ops.FillVar;
						ins.Dest = Conv2 (to);
						ins.Const0 = vsFrom.spillSlot;
					} else {
						throw new Exception ("NRIEjid");
					}
					
				}

			}

			KillVar2 (to);
		}

		public void BinOp (Ins ins, int dest, int r0, int r1) {
			var vsDest = varState [dest];
			//Binop follows x86 rules of r0 getting clobbered.
			if (!vsDest.IsLive)
				throw new Exception ($"Dead var or bug? dest: {vsDest}");

			var vsR0 = varState [r0];
			if (!vsR0.IsLive)
				Assign3 (r0, vsDest);
			else
				throw new Exception ("R0 needs extra care as it outlives the binop");

			var vsR1 = varState [r1];
			if (!vsR1.IsLive) {
				var inUse = new HashSet<Register> ();
				if (vsDest.IsReg)
					inUse.Add (vsDest.reg);
				if (vsR0.IsReg)
					inUse.Add (vsR0.reg);

				SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
				reqs.Add (new AllocRequest (r1));

				DoAlloc (reqs, inUse);
			}

			ins.Dest = Conv2 (dest);
			ins.R0 = Conv2 (r0);
			ins.R1 = Conv2 (r1);

			KillVar2 (dest);
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

		bool[] spillSlots;
		int spillSlotMax = -1;
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

		VarState ForceSpill (AllocRequest ar) {
			Console.WriteLine ($"\tspilling {ar}");
			int slot = AllocSpillSlot (ar.preferred != null ? ar.preferred.Value.spillSlot : -1);
			ar.SetResult (new VarState (slot));

			var res = new VarState (slot);
			varState [ar.vreg] = res;
			return res;
		}

		VarState FindOrSpill (AllocRequest ar, HashSet<Register> inUse, ref Ins spillIns) {
			var s = string.Join (",", regToVar.Where (reg => reg != -1).Select ((reg,idx) => $"{(Register)idx} -> {reg}"));
			var iu = string.Join (",", inUse.Select (r => r.ToString ()));
			Console.WriteLine ($"FindOrSpill R2V ({s}) inUse ({iu})");

			int vreg = ar.vreg;

			if (ar.preferred != null) {
				var pref = ar.preferred.Value;
				if (pref.IsReg && regToVar [(int)pref.reg] == -1)
					return Assign2 (vreg, pref.reg, inUse);
				if (ar.canSpill && pref.IsSpill)
					return ForceSpill (ar);
			}

			for (int i = 0; i < CallConv.caller_saved.Length; ++i) {
				Register candidate = CallConv.caller_saved [i];
				if (regToVar [(int)candidate] == -1)
					return Assign2 (vreg, candidate, inUse);
			}

			for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
				Register candidate = CallConv.callee_saved [i];
				if (regToVar [(int)candidate] == -1)
					return Assign2 (vreg, candidate, inUse);
			}
			Console.WriteLine ($"find reg: spilling! ({s})");
			int spillVreg = -1, regularVreg = -1;

			for (int i = 0; i < (int)Register.RegCount; ++i) {
				int candVreg = regToVar [i];
				if (candVreg == -1) //ignored for some reason
					continue;
				if (inUse.Contains ((Register)i)) //can't spill active regs
					continue;
				Console.WriteLine ($"\tcandidate: {(Register)i}");
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
				Assign2 (vreg, reg, inUse);
				Console.WriteLine ($"\tpicked spillVreg vreg {spillVreg} reg {reg}");
				return new VarState (reg);
			}

			if (regularVreg != -1) {
				var reg = varState [regularVreg].reg;
				varState [regularVreg].reg = Register.None;
				varState [regularVreg].spillSlot = AllocSpillSlot (-1);
				var ins = new Ins (Ops.FillVar) {
					Dest = MaskReg (reg),
					Const0 = varState [regularVreg].spillSlot,
				};
				ins.SetNext (spillIns);
				spillIns = ins;
				Assign2 (vreg, reg, inUse);
				Console.WriteLine ($"\tpicked regularVreg vreg {regularVreg} reg {reg}");
				return new VarState (reg);
			}

			throw new Exception ("FindOrSpill failed");
		}

		void DoAlloc (SortedSet<AllocRequest> reqs, HashSet<Register> inUse = null) {
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
						ForceSpill (ar);
						--in_use;
					}
					if (in_use <= REG_MAX)
						break;
				}
			}

			if (inUse == null)
				inUse = new HashSet<Register> ();
			Ins spillIns = null;
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

		public void CondBranch (Ins ins, int r0, int r1, CallInfo[] infos) {
			SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
			for (int j = 0; j < infos.Length; ++j)
				this.CallInfo2 (infos [j], reqs);

			reqs.Add (new AllocRequest (r0));
			reqs.Add (new AllocRequest (r1));

			DoAlloc (reqs);

			ins.R0 = Conv2 (r0);
			ins.R1 = Conv2 (r1);

			for (int j = 0; j < infos.Length; ++j)
				this.SetCallInfoResult (infos [j]);
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
					if (!thisVS.Eq (targetVS))
						repairing.Add (Tuple.Create (targetVS, thisVS));
				}
				if (repairing.Count > 0)
					EmitRepairCode2 (info.Target, repairing);

			}
		}

		static void EmitRepairCode2 (BasicBlock bb, List<Tuple<VarState, VarState>> repairing) {
			var table = String.Join (",", repairing.Select (t => $"{t.Item1} => {t.Item2}"));
			Console.WriteLine ($"REPAIRING WITH {table}");
			/*CI allocation only requires repairing when the current BB has multiple out edges
			so we always repair on the target.
			We can only repair on the target if it has a single incomming BB.
			What this mean is that we might need to remove a critical-edge at this point. TBD
			*/

			if (bb.From.Count > 1)
				throw new Exception ("Can't handle critical edges yet");

			if (repairing.Count > 1)
				throw new Exception ("Need to figure out how to compute repair optimal swapping");

			//One var repair
			if (repairing [0].Item2.IsSpill) {
				if (repairing [0].Item1.IsSpill)
					throw new Exception ($"DONT KNOW HOW TO REPAIR mem2mem {repairing [0]}");
				bb.Prepend (new Ins (Ops.FillVar) {
					Dest = MaskReg (repairing [0].Item1.reg),
					Const0 = repairing [0].Item2.spillSlot
				});
			} else {
				if (repairing [0].Item1.IsSpill)
					throw new Exception ($"IMPLEMENT ME reg2mem repainging {repairing [0]}");
				bb.Prepend (new Ins (Ops.Mov) {
					Dest = MaskReg (repairing [0].Item1.reg),
					R0 = MaskReg (repairing [0].Item2.reg)
				});
			}

			for (int i = 0; i < bb.InVarState.Count; ++i) {
				if (bb.InVarState [i].Eq (repairing [0].Item1)) {
					bb.InVarState [i] = repairing [0].Item2;
					break;
				}
			}
		}

		void CallInfo2 (CallInfo info, SortedSet<AllocRequest> reqs) {
			if (info.Target.InVarState == null) { //This happens the loop tail-> loop head edge.
				for (int i = 0; i < info.Args.Count; ++i) {
					reqs.Add (new AllocRequest (info.Args [i], true, null));
				}
			} else {
				for (int i = 0; i < info.Args.Count; ++i) {
					reqs.Add (new AllocRequest (info.Args [i], true, info.Target.InVarState [i]));
				}
			}
		}

		public void DirectBranch (Ins ins, CallInfo[] infos) {
			SortedSet<AllocRequest> reqs = new SortedSet<AllocRequest> ();
			for (int j = 0; j < infos.Length; ++j)
				this.CallInfo2 (infos [j], reqs);

			DoAlloc (reqs);

			for (int j = 0; j < infos.Length; ++j)
				this.SetCallInfoResult (infos [j]);
		}

		public void SetRet (Ins ins, int vreg) {
			if (varState [vreg].IsLive)
				throw new Exception ("SetReg MUST be the last use of the vreg on its BB");

			Register reg = CallConv.ReturnReg;
			if (regToVar [(int)reg] >= 0)
				throw new Exception ("For some reason, someone is already using SetReg's register. Fix your IR!");

			Assign2 (vreg, reg, null);

			ins.R0 = Conv2 (vreg);
		}

		public void LoadArg (Ins ins, int dest, int position) {
			Register reg = CallConv.RegForArg (position);
			var vs = varState [dest];

			if (!vs.IsReg || vs.reg != reg) {
				Console.WriteLine ($"Need to fixup income reg. I want {reg} but have {vs}");
				if (regToVar [(int)reg] >= 0)
					throw new Exception ($"the var we want is fucked {regToVar [(int)reg]}");

				if (vs.IsReg) {
					ins.Op = Ops.Mov;
					ins.Dest = MaskReg (vs.reg);
					ins.R0 = MaskReg (reg);					
				} else {
					ins.Op = Ops.SpillVar;
					ins.R0 = MaskReg (reg);
					ins.Const0 = vs.spillSlot;
				}

			}
		}

		void Use (int var, Register preference, List<Tuple<Register,Register>> repairing) {
			// Console.WriteLine ($"*Use ${var} pref ${preference}");
			if (regToVar [(int)preference] == -1) {
				Assign (var, preference);
			} else {
				var reg = Alloc (var);
				repairing.Add (Tuple.Create (preference, reg));
			}
		}

		void RepairInfo (BasicBlock bb, BasicBlock from, CallInfo info) {
			Console.WriteLine ($"REPAIRING THE LINK BB{from.Number} to BB{bb.Number}");

			info.NeedRepairing = false;
			var repairing = new List<Tuple<VarState, VarState>> ();
			for (int i = 0; i < info.Args.Count; ++i) {
				// Register source = info.Args [i].UnmaskReg ();
				var source = info.AllocResult [i];

				if (!bb.InVarState [i].Eq (source))
					repairing.Add (Tuple.Create (source, bb.InVarState [i]));
			}
			EmitRepairCode2 (bb, repairing);
		}

		public int Finish () {
			bb.InVarState = new List<VarState> ();
			for (int i = 0; i < bb.InVars.Count; ++i) {
				var vs = varState [i];
				if (!vs.IsLive)
					throw new Exception ($"Bad REGALLOC didn't allocate vreg {i}!");
				bb.InVarState.Add (vs);
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

				return $"RA! ({s})";
			}
		}
	}

	public class CallInfo {
		public List<int> Args { get; set; }
		public List<VarState> AllocResult { get; set; }

		public BasicBlock Target { get; set; }
		public bool NeedRepairing { get; set; }

		public CallInfo (Dictionary <int, int> varTable, BasicBlock target) {
			Args = new List<int> ();
			Console.WriteLine ("Computing translation table to BB{0}", target.Number);
			foreach (var v in target.InVars) {
				Console.WriteLine ("\tlooking up var {0}", v);
				Args.Add (varTable [v]);
			}
			Target = target;
		}

		public override string ToString () {
			var vars = string.Join (",", Args.Select (a => a.V2S ()));
			if (AllocResult != null) {
				var allocs = string.Join (",", AllocResult.Select (a => a.ToString ()));
				return $"(BB{Target.Number}, ({vars}), {allocs})";
			}
			return $"(BB{Target.Number}, ({vars}))";
		}
	}

	public class Ins {
		public Ins (Ops op) {
			this.Op = op;
			Dest = R0 = R1 = -100;
		}
		public Ops Op { get; set; }
		public int Dest { get; set; }
		public int R0 { get; set; }
		public int R1 { get; set; }
		public int Const0 { get; set; }
		public int Const1 { get; set; }
		public CallInfo[] CallInfos { get; set; }

		public Ins Prev { get; private set; }
		public Ins Next { get; private set; }

		string DStr {
			get {
				return Dest.V2S ();
			}
		}

		string R0Str {
			get {
				return R0.V2S ();
			}
		}

		string R1Str {
			get {
				return R1.V2S ();
			}
		}

		public override string ToString () {
			switch (Op) {
			case Ops.IConst:
				return $"{Op} {DStr} <= {Const0}";
			case Ops.Mov:
				return $"{Op} {DStr} <= {R0Str}";
			case Ops.Ble:
			case Ops.Blt:
				return $"{Op} {R0Str} {R1Str} {CallInfos[0]} {CallInfos[1]}";
			case Ops.Br:
				return $"{Op} {CallInfos[0]}";
			case Ops.LoadArg:
				return $"{Op} {DStr} <= REG_ARG {Const0}";
			case Ops.SetRet:
				return $"{Op} {R0Str}";
			case Ops.Nop:
				return $"Nop";
			case Ops.Add:
				return $"{Op} {DStr} <= {R0Str} {R1Str}";
			case Ops.FillVar:
				return $"{Op} {DStr} <= [{Const0}]";
			case Ops.SpillVar:
				return $"{Op} [{Const0}] <= {R0Str}";
			case Ops.SpillConst:
				return $"{Op} [{Const0}] <= {Const1}";
			default:
				return $"{Op} {DStr} <= {R0Str} {R1Str} #FIXME";
			}
		}

		public void SetNext (Ins i) {
			i.Prev = this;
			this.Next = i;
		}
	}

public class BasicBlock {
	public int Start { get; set; }
	public int End { get; set; }
	public int Number { get; private set; }
	public BasicBlock NextInOrder { get; set; }
	List <BasicBlock> from = new List <BasicBlock> ();
	List <BasicBlock> to = new List <BasicBlock> ();

	internal ISet<int> InVars = new SortedSet<int> ();
	internal ISet<int> DefVars = new HashSet<int> ();
	public List<VarState> InVarState { get; set; }

	Ins first, last;
	int reg;

	public Ins FirstIns { get { return first; } }
	public Ins LastIns { get { return last; } }

	public int NextReg () {
		return reg++;
	}

	public int MaxReg () {
		return reg;
	}

	public void Append (Ins ins) {
		if (first == null) {
			first = last = ins;
		} else {
			last.SetNext (ins);
			last = ins;
		}
	}

	public void Prepend (Ins ins) {
		if (first == null) {
			first = last = ins;
		} else {
			ins.SetNext (first);
			first = ins;
		}
	}


	public BasicBlock (Compiler c) {
		Number = c.bb_number++;
	}

	public IReadOnlyList <BasicBlock> To { get { return to; } }
	public IReadOnlyList <BasicBlock> From { get { return from; } }
	public bool Enqueued { get; set; }
	public bool Done { get; set; }
	public bool NeedRepairing { get; set; }

	public override string ToString () {
		var fromStr = string.Join (",", from.Select (bb => bb.Number.ToString ()));
		var toStr = string.Join (",", to.Select (bb => bb.Number.ToString ()));
		var inVars = string.Join (",", InVars);
		var defVars = string.Join (",", DefVars);
		string body = "";
		if (first != null) {
			body = ":\n";
			for (Ins c = first; c != null; c = c.Next) {
				if (c.Prev != null)
					body += "\n";
				body += $"\t{c}";
			}
		}
		string ra = "";
		if (InVarState != null) {
			var regset = string.Join (",", InVarState.Select (r => r.ToString ()));
			ra = $" RA ({regset})";
		}
		return $"BB ({Number}) [0x{Start:X} - 0x{End:X}] FROM ({fromStr}) TO ({toStr}) IN-VARS ({inVars}) DEFS ({defVars}){ra}{body}";
	}

	public BasicBlock SplitAt (Compiler c, int offset, bool link) {
		if (offset < Start)
			throw new Exception ($"Can't split {this} at {offset:X}, it's before");
		if (offset == Start) {
			return this;
		}
		if (offset < End) {
			var next = new BasicBlock (c) {
				Start = offset,
				End = this.End,
				NextInOrder = this.NextInOrder
			};
			to.AddRange (this.to);

			this.End = offset;
			this.NextInOrder = next;
			this.to.Clear ();
			if (link)
				LinkTo (next);
			return next;
		} else if (NextInOrder != null && NextInOrder.Start == offset) {
			if (link)
				LinkTo (NextInOrder);
			return NextInOrder;
		}

		// Console.WriteLine ("===FUUU BBs:");
		// BasicBlock current;
		// for (current = c.first_bb; current != null; current = current.NextInOrder) {
		// 	Console.WriteLine (current);
		// }
		throw new Exception ($"Can't split {this} at 0x{offset:X}, it's after");
	}

	public void LinkTo (BasicBlock targetBB) {
		to.Add (targetBB);
		targetBB.from.Add (this);
		// Console.WriteLine ($"LINKING {Number} To {targetBB.Number}");
	}

	public void UnlinkTo (BasicBlock targetBB) {
		to.Remove (targetBB);
		targetBB.from.Remove (this);
		// Console.WriteLine ($"LINKING {Number} To {targetBB.Number}");
	}

	public void MakeFirstDest (BasicBlock bb) {
		if (to[0] != bb) {
			to.Remove (bb);
			to.Insert (0, bb);
		}
	}
	public BasicBlock Find (int offset) {
		BasicBlock current;
		for (current = this; current != null; current = current.NextInOrder) {
			if (offset >= current.Start && offset < current.End)
				return current;
		}
		// Console.WriteLine ("===FUUU BBs:");
		// for (current = this; current != null; current = current.NextInOrder) {
		// 	Console.WriteLine (current);
		// }

		throw new Exception ($"Could not find BB at 0x{offset:X}");
	}
}

public class Compiler {
	MethodData method;
	MethodBody body;
	internal BasicBlock first_bb, epilogue;
	internal int bb_number;

	public Compiler (MethodData method) {
		this.method = method;
		Console.WriteLine ("compiling {0}", method.Name);
	}

	public void DumpBB (string why) {
		BasicBlock current;
		Console.WriteLine ("===BBs: ({0})", why);
		for (current = first_bb; current != null; current = current.NextInOrder) {
			Console.WriteLine (current);
		}
	}

	void ComputeBasicBlocks () {
		var current = new BasicBlock (this) {
			Start = 0,
			End = body.Body.Length,
		};

		Console.WriteLine ("=== BB formation");
		first_bb = current;

		epilogue = new BasicBlock (this) {
			Start = body.Body.Length,
			End = body.Body.Length,
		};

		var it = body.GetIterator ();
		while (it.MoveNext ()) {
			Console.WriteLine ("0x{0:X}: {1} [{2}]", it.Index, it.Mnemonic, it.Flags & OpcodeFlags.FlowControlMask);
			switch (it.Flags & OpcodeFlags.FlowControlMask) {
			case OpcodeFlags.Next:
			case OpcodeFlags.Call:
				if (it.Opcode == Opcode.Jmp)
					throw new Exception ("no support for jmp");
				if (it.NextIndex >= current.End) {
					current.LinkTo (current.NextInOrder);
					current = current.NextInOrder;
				}
				break; //nothing to do here
			case OpcodeFlags.Branch: {
				var target_offset = it.NextIndex + it.DecodeParamI ();
				var next = current.SplitAt (this, it.NextIndex, link: false);
				var target = first_bb.Find (target_offset).SplitAt (this, target_offset, link: false);
				current.LinkTo (target);
				if (target != next)
					current.UnlinkTo (next);
				current = next;
				break;
			}

			case OpcodeFlags.CondBranch: {
				//XXX ensure that target is the first element
				var target_offset = it.NextIndex + it.DecodeParamI ();
				var next = current.SplitAt (this, it.NextIndex, link: true);
				var target = first_bb.Find (target_offset).SplitAt (this, target_offset, link: true);
				current.LinkTo (target);
				current.MakeFirstDest (target);
				current = next;
				break;
			}
			case OpcodeFlags.Return: {
				if (it.HasNext) {
					var next = current.SplitAt (this, it.NextIndex, link: false);
					current.LinkTo (epilogue);
					current = next;
				} else {
					current.LinkTo (epilogue);
				}
				break;
			}

			case OpcodeFlags.Throw: {
				throw new Exception ("NO EH");
			}
			default:
				throw new Exception ($"Invalid opcode flags {it.Flags}");
			}
		}
		current.NextInOrder = epilogue;
		
		DumpBB ("BB Formation");
	}

	static void AddUse (ISet<int> inVars, ISet<int> defs, int var) {
		if (!defs.Contains (var)) {
			Console.WriteLine ($"\t\tFound use of {var}");
			inVars.Add (var);
		}
	}

	static void AddDef (ISet<int> inVars, ISet<int> defs, int var) {
		Console.WriteLine ($"\t\tFound def of {var}");
		defs.Add (var);
	}

	IlIterator GetILIterator (BasicBlock bb) {
		return new IlIterator (body.Body, bb.Start, bb.End);
	}

	void EmitBasicBlockBodies () {
		var list = new List<BasicBlock> (); //FIXME use a queue collection

		Console.WriteLine ("=== BB code emit");
		BasicBlock current;
		//First to last we compute income regs due unknown uses

		if (method.Signature.ReturnType != ClrType.Void)
			epilogue.InVars.Add (0);

		for (current = first_bb; current != null; current = current.NextInOrder) {
			Console.WriteLine ($"processing {current}");

			var it = GetILIterator (current);

			// HashSet<int> uses = new HashSet<int> ();
			// HashSet<int> defs = new HashSet<int> ();

			while (it.MoveNext ()) {
				Console.WriteLine ("\t[{0}]:{1}", it.Index, it.Mnemonic);
				switch (it.Opcode) {
				case Opcode.Stloc0:
					AddDef (current.InVars, current.DefVars, 1);
					break;
				case Opcode.Stloc1:
					AddDef (current.InVars, current.DefVars, 2);
					break;
				case Opcode.Stloc2:
					AddDef (current.InVars, current.DefVars, 3);
					break;
				case Opcode.Stloc3:
					AddDef (current.InVars, current.DefVars, 4);
					break;
				case Opcode.StlocS:
					AddDef (current.InVars, current.DefVars, 1 + it.DecodeParamI ());
					break;
				case Opcode.Ldloc0:
					AddUse (current.InVars, current.DefVars, 1);
					break;
				case Opcode.Ldloc1:
					AddUse (current.InVars, current.DefVars, 2);
					break;
				case Opcode.Ldloc2:
					AddUse (current.InVars, current.DefVars, 3);
					break;
				case Opcode.Ldloc3:
					AddUse (current.InVars, current.DefVars, 4);
					break;
				case Opcode.LdlocS:
					AddUse (current.InVars, current.DefVars, 1 + it.DecodeParamI ());
					break;
				case Opcode.StargS:
					AddDef (current.InVars, current.DefVars, -1 - it.DecodeParamI ());
					break;
				case Opcode.Ldarg0:
					AddUse (current.InVars, current.DefVars, -1);
					break;
				case Opcode.Ldarg1:
					AddUse (current.InVars, current.DefVars, -2);
					break;
				case Opcode.Ldarg2:
					AddUse (current.InVars, current.DefVars, -3);
					break;
				case Opcode.Ldarg3:
					AddUse (current.InVars, current.DefVars, -4);
					break;
				case Opcode.Ret:
					AddDef (current.InVars, current.DefVars, 0);
					break;
				}
			}
			//Queue leaf nodes as they are all ready
			if (current.To.Count == 0) {
				list.Add (current);
				current.Enqueued = true;
			}
		}

		DumpBB ("Before converge");

		while (list.Count > 0) {
			current = list [0];
			list.RemoveAt (0);
			current.Enqueued = false;
			int inC = current.InVars.Count;

			Console.WriteLine ($"iterating BB{current.Number}");

			foreach (var next in current.To) {
				foreach (var v in next.InVars) {
					if (!current.DefVars.Contains (v))
						current.InVars.Add (v);
				}
			}

			current.Done = true;
			bool modified = inC != current.InVars.Count;
			
			foreach (var prev in current.From) {
				if (!prev.Enqueued && (!prev.Done || modified)) {
					list.Add (prev);
					prev.Enqueued = true;
					prev.Done = false;
				}
			}
		}
		//compute initial candidates, emit in reverse dominator order
		DumpBB ("Compute INVARS");

		//First to last we compute income regs due unknown uses
		for (current = first_bb; current != null; current = current.NextInOrder) {
			EmitBlockBody (current);
		}

		DumpBB ("EmitIR done");
	}

	static Ops CilToBinop (Opcode op) {
		switch (op) {
		case Opcode.Add: return Ops.Add;
		default: throw new Exception ($"{op} is not a binop");
		}
	}

	static Ops CilToCondOp (Opcode op) {
		switch (op) {
		case Opcode.Ble: return Ops.Ble;
		case Opcode.Blt: return Ops.Blt;
		default: throw new Exception ($"{op} is not a condop");
		}
	}

	public class EvalStack  {
		Stack <Ins> stack = new Stack <Ins> ();
		BasicBlock bb;

		public EvalStack (BasicBlock bb) {
			this.bb = bb;
		}

		public void PushInt (int c) {
			Console.WriteLine ("PushInt {0}", c);

			var i = new Ins (Ops.IConst) {
				Dest = bb.NextReg (),
				Const0 = c,
			};
			stack.Push (i);
			bb.Append (i);
		}

		public int StoreVar () {
			Console.WriteLine ("StoreVar");

			var r0 = stack.Pop ().Dest;
			var i = new Ins (Ops.Mov) {
				Dest = bb.NextReg (),
				R0 = r0,
			};
			stack.Push (i);
			bb.Append (i);
			return i.Dest;
		}

		public void LoadVar (int v) {
			Console.WriteLine ("LoadVar {0}", v);
			var i = new Ins (Ops.Mov) {
				Dest = bb.NextReg (),
				R0 = v,
			};
			stack.Push (i);
			bb.Append (i);
		}

		public void PushBinOp (Opcode op) {
			Console.WriteLine ("BinOp {0}", op);
			var r1 = stack.Pop ().Dest;
			var r0 = stack.Pop ().Dest;
			var i = new Ins (CilToBinop (op)) {
				Dest = bb.NextReg (),
				R0 = r0,
				R1 = r1,
			};
			stack.Push (i);
			bb.Append (i);
		}

		public void EmitCondBranch (Opcode cond, CallInfo[] infos) {
			Console.WriteLine ("CondBranch {0}", cond);

			var r1 = stack.Pop ().Dest;
			var r0 = stack.Pop ().Dest;
			var i = new Ins (CilToCondOp (cond)) {
				R0 = r0,
				R1 = r1,
				CallInfos = infos,
			};

			bb.Append (i);
		}

		public void EmitBranch (CallInfo info) {
			Console.WriteLine ("Branch");

			var i = new Ins (Ops.Br) {
				CallInfos = new CallInfo[] { info },
			};
			bb.Append (i);
		}
	}
	
	void EmitBlockBody (BasicBlock bb)
	{
		Console.WriteLine ("Emitting body of BB{0}", bb.Number);
		var varTable = new Dictionary <int, int> ();
		Console.WriteLine ("initial var map:");
		foreach (var v in bb.InVars) {
			varTable [v] = bb.NextReg ();
			Console.WriteLine ($"\t{v} == {varTable [v]}");
		}

		var it = GetILIterator (bb);
		var s = new EvalStack (bb);
		bool done = false;
		while (it.MoveNext ()) {
			switch (it.Opcode) {
			case Opcode.Nop: //Right now nothing, later, seqpoints
				break;
			case Opcode.Add:
				s.PushBinOp (it.Opcode);
				break;
			case Opcode.LdcI4_0:
				s.PushInt (0);
				break;
			case Opcode.LdcI4_1:
				s.PushInt (1);
				break;
			case Opcode.LdcI4_2:
				s.PushInt (2);
				break;
			case Opcode.LdcI4_3:
				s.PushInt (3);
				break;
			case Opcode.LdcI4_4:
				s.PushInt (4);
				break;
			case Opcode.LdcI4_5:
				s.PushInt (5);
				break;
			case Opcode.LdcI4S:
				s.PushInt (it.DecodeParamI ());
				break;
			case Opcode.Stloc0:
				varTable [1] = s.StoreVar ();
				break;
			case Opcode.Stloc1:
				varTable [2] = s.StoreVar ();
				break;
			case Opcode.Stloc2:
				varTable [3] = s.StoreVar ();
				break;
			case Opcode.Stloc3:
				varTable [4] = s.StoreVar ();
				break;
			case Opcode.StlocS:
				varTable [1 + it.DecodeParamI ()] = s.StoreVar ();
				break;
			case Opcode.Ldloc0:
				s.LoadVar (varTable [1]);
				break;
			case Opcode.Ldloc1:
				s.LoadVar (varTable [2]);
				break;
			case Opcode.Ldloc2:
				s.LoadVar (varTable [3]);
				break;
			case Opcode.Ldloc3:
				s.LoadVar (varTable [4]);
				break;
			case Opcode.LdlocS:
				s.LoadVar (varTable [1 + it.DecodeParamI ()]);
				break;
			case Opcode.Ldarg0:
				s.LoadVar (varTable [-1]);
				break;
			case Opcode.Ldarg1:
				s.LoadVar (varTable [-2]);
				break;
			case Opcode.Ldarg2:
				s.LoadVar (varTable [-3]);
				break;
			case Opcode.Ldarg3:
				s.LoadVar (varTable [-4]);
				break;
			case Opcode.StargS:
				varTable [-1 - it.DecodeParamI ()] = s.StoreVar ();
				break;

			case Opcode.Blt:
			case Opcode.Ble:
				Console.WriteLine ("varTable before cond:");
				foreach (var kv in varTable)
					Console.WriteLine ($"\t{kv.Key} -> {kv.Value}");

				var infos = new CallInfo [2];
				infos [0] = new CallInfo (varTable, bb.To [0]);
				infos [1] = new CallInfo (varTable, bb.To [1]);

				s.EmitCondBranch (it.Opcode, infos);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;
			case Opcode.Br:
				Console.WriteLine ($"BB TO LEN {bb.To.Count}");
				s.EmitBranch (new CallInfo (varTable, bb.To [0]));
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;
			case Opcode.Ret:
				if (method.Signature.ReturnType != ClrType.Void)
					varTable [0] = s.StoreVar ();
				s.EmitBranch (new CallInfo (varTable, bb.To [0]));
				if (it.HasNext)
					throw new Exception ("Ret MUST be last op in a BB");
				done = true;
				break;

			default:
				throw new Exception ($"Cannot emit {it.Mnemonic}");
			}
		}
		if (!done) {
			if (bb.To.Count > 1)
				throw new Exception ("Can't fall through to multiple blocks");
			if (bb.To.Count == 1)
				s.EmitBranch (new CallInfo (varTable, bb.To [0]));
		}
	}

	SortedSet<Register> callee_saved = new SortedSet<Register> ();
	int spillAreaUsed = 0;

	void RegAlloc (BasicBlock bb) {
		Console.WriteLine ("Allocating BB{0}", bb.Number);
		var ra = new RegAllocState (bb, callee_saved);

		for (Ins ins = bb.LastIns; ins != null; ins = ins.Prev) {
			Console.WriteLine ($"Before {ins.ToString ()}");
			switch (ins.Op) {
			case Ops.IConst:
				ra.Def (ins, ins.Dest);
				break;
			case Ops.Mov:
				ra.Move (ins, ins.Dest, ins.R0);
				break;
			case Ops.Add:
				ra.BinOp (ins, ins.Dest, ins.R0, ins.R1);
				break;
			case Ops.Ble:
			case Ops.Blt:
				ra.CondBranch (ins, ins.R0, ins.R1, ins.CallInfos);
				break;
			case Ops.Br:
				ra.DirectBranch (ins, ins.CallInfos);
				break;
			case Ops.SetRet:
				ra.SetRet (ins, ins.R0);
				break;
			case Ops.LoadArg:
				ra.LoadArg (ins, ins.Dest, ins.Const0);
				break;
			default:
				throw new Exception ($"Don't now how to reg alloc {ins}");
			}
			Console.WriteLine ($"After {ins.ToString ()}\n\t{ra.State}");
		}
		spillAreaUsed= Math.Max (spillAreaUsed, ra.Finish ());
		// DumpBB ("AFTER RA OF BB"+bb.Number);
		// Console.WriteLine ("AFTER RA:\n{0}", bb);
	}

	/*
	How does regalloc work?
   
	Challlenges:
		Figure out pressure and spill. The BB params approach makes repair easy for
		register assignment but tricky for allocation.

	# Coloring
	Local backwards pass:
		Good with use constrains (r0 clobber, reg-pairs, etc)
		Sucks at handling def constrains (reg args, call returns, etc)
		Indiferent to interference due to, e.g. call clobbered regs.

	Local forwards pass:
		It's the converse of the backwards pass. It helps with reg args and call returns.

	The global pass has a similar issue, every time you have a BB with more than one entry or exit,
	one of the directions will suffer. In general, repair is easy as long as we don't have a critical edge (which requires breaking).

	In practice, we only care about coloring of loops carried variables. For that we need to figure out how to reduce the chance
	of healing.

	# Spilling

	Spilling present an interesting problem. Figuring out the var to spill and do spill/fill insertion is really tricky.
	The extra challenge of this design is that we lose global visiblity so spilling across blocks means a later decision could reduce
	the color number of a BB if the spilled var is "pass through".

	This will require empirial experimentation.

	How do we find what are the loop carried vars?
		We can pick vars from the exit branch but not on the continue branch. Those are clearly Loop Invariant Variables.
	*/
	void RegAlloc () {
		Console.WriteLine ("DOING REGALLOC");

		//Insert fake instructions for reg args
		
		//XXX right now we have a fixed 2 int args
		if (first_bb.From.Count > 0)
			throw new Exception ("The first BB MUST NOT have predecessors");

		int var_count = this.method.Signature.ParamCount;
		for (int i = var_count - 1; i >= 0; --i) {
			first_bb.Prepend (
				new Ins (Ops.LoadArg) {
					Dest = i,
					Const0 = i //FIXME connect this to the calling convention as the register arg position is arbitrary
				});
		}

		if (method.Signature.ReturnType != ClrType.Void) {
			epilogue.Append (new Ins (Ops.SetRet) {
				R0 = 0
			});
		}

		var list = new List<BasicBlock> (); //FIXME use a queue collection

		for (var current = first_bb; current != null; current = current.NextInOrder) {
			current.Enqueued = current.Done = false;
			if (current.To.Count == 0) {
				list.Add (current);
				current.Enqueued = true;
			}
		}

		while (list.Count > 0) {
			var current = list [0];
			list.RemoveAt (0);
			current.Enqueued = false;
			current.Done = true;

			RegAlloc (current);

			foreach (var prev in current.From) {
				if (!prev.Enqueued && !prev.Done) {
					list.Add (prev);
					prev.Enqueued = true;
				}
			}
		}
		DumpBB ("AFTER REGALLOC");
	}

	static string BranchOpToJmp (Ops op) {
		switch (op) {
		case Ops.Ble: return "jle";
		case Ops.Blt: return "jl";
		default: throw new Exception ($"Not a branch op {op}");
		}
	}

	void CodeGen () {
		Console.WriteLine ("--- CODEGEN");

		//hardcoded prologue
		Console.WriteLine ("\tpushq %rbp");
		Console.WriteLine ("\tmovq %rsp, %rbp");

		//Emit save
		int stack_space = (callee_saved.Count + spillAreaUsed) * 8;
		int spillOffset = 8;
		if (stack_space > 0) {
			Console.WriteLine ($"\tsubq 0x${stack_space:X}, %rsp");
			int idx = 8;
			foreach (var reg in callee_saved) {
				Console.WriteLine ($"\tmovq ${reg.ToString ().ToLower ()}, -0x{idx:X}(%rbp)");
				idx += 8;
			}
			spillOffset = idx;
		}

		for (var bb = first_bb; bb != null; bb = bb.NextInOrder) {
			Console.WriteLine ($"BB{bb.Number}:");

			for (Ins ins = bb.FirstIns; ins != null; ins = ins.Next) {
				switch (ins.Op) {
				case Ops.Mov:
					if (ins.Dest != ins.R0)
						Console.WriteLine ($"\tmovq %{ins.R0.V2S().ToLower ()}, %{ins.Dest.V2S().ToLower ()}");
					break;
				case Ops.IConst: {
					var str = ins.Const0.ToString ("X");
					Console.WriteLine ($"\tmov $0x{str}, %{ins.Dest.V2S().ToLower ()}");
					break;
				}
				case Ops.Ble:
				case Ops.Blt: {
					var mi = BranchOpToJmp (ins.Op);
					Console.WriteLine ($"\tcmp %{ins.R0.V2S().ToLower ()}, %{ins.R1.V2S().ToLower ()}");
					Console.WriteLine ($"\t{mi} $BB{ins.CallInfos[0].Target.Number}");
					break;
				} case Ops.Br:
					if (ins.CallInfos[0].Target != bb.NextInOrder)
						Console.WriteLine ($"\tjmp $BB{ins.CallInfos[0].Target.Number}");
					break;
				case Ops.Add:
					if (ins.Dest != ins.R0)
						throw new Exception ("Bad binop encoding!");
					Console.WriteLine ($"\taddl %{ins.Dest.V2S().ToLower ()}, %{ins.R1.V2S().ToLower ()}");
					break;
				case Ops.SetRet:
				case Ops.Nop:
					break;
				case Ops.SpillVar: {
					int offset = spillOffset + ins.Const0 * 8;
					Console.WriteLine ($"\tmovq ${ins.R0.V2S().ToLower ()}, -0x{(offset):X}(%rbp)");
					break;
				}
				case Ops.FillVar: {
					int offset = spillOffset + ins.Const0 * 8;
					Console.WriteLine ($"\tmovq -0x{offset:X}(%rbp), ${ins.Dest.V2S().ToString ().ToLower ()}");
					break;
				}
				default:
					throw new Exception ($"Can't code gen {ins}");
				}
			}
		}

		//hardcoded epilogue

		//Emit restore
		if (callee_saved.Count > 0) {
			int idx = 8;
			foreach (var reg in callee_saved) {
				Console.WriteLine ($"\tmovq -0x{idx:X}(%rbp), ${reg.ToString ().ToLower ()}");
				idx += 8;
			}
		}

		Console.WriteLine ("\tleave");
		Console.WriteLine ("\tretq");

	}
	public void Run () {
		/*
		Compilation pipeline:
		
		BB formation

		Compute dominators -- xxx not sure I'll need this info for global placement in BBs
		Translate IR
		*Optimize

		RA
		Codegen

		*/

		this.body = method.GetBody ();
		Console.WriteLine ("\t{0}", body);

		//step 1, BB formation
		ComputeBasicBlocks ();

		//step 2, emit BBs in reverse order
		EmitBasicBlockBodies ();

		//step 3, optimizations (TODO)

		//step 4, insel, scheduling

		//step 5, spill & reg alloc
		RegAlloc ();

		//step 6, codegen
		CodeGen ();
	}
}

}