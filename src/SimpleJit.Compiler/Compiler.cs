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
			Register.RDX,
			Register.RSI,
			Register.RDI,
			Register.R8,
			Register.R9,
			Register.R10,
		};

		public static readonly Register[] callee_saved = new Register[] {
			Register.RBX,
			Register.R12,
			Register.R13,
			Register.R14,
			Register.R15,
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
		SetRet
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

	class RegAllocState {
		Register[] varToReg;
		int[] regToVar;
		BasicBlock bb;
		SortedSet<Register> callee_saved;

		public RegAllocState (BasicBlock bb, SortedSet<Register> callee_saved) {
			varToReg = new Register [bb.MaxReg ()];
			for (int i = 0; i < varToReg.Length; ++i)
				varToReg [i] = Register.None;
			regToVar = new int [(int)Register.RegCount];
			for (int i = 0; i < (int)Register.RegCount; ++i)
				regToVar [i] = -1;
			this.bb = bb;
			this.callee_saved = callee_saved;
		}

		void Assign (int var, Register reg) {
			regToVar [(int)reg] = var;
			varToReg [var] = reg;
		}

		Register FindReg () {
			// var s = string.Join (",", regToVar.Select (v => v.ToString ()));
			// Console.WriteLine ($"find reg: ({s})");
			for (int i = 0; i < CallConv.caller_saved.Length; ++i) {
				Register candidate = CallConv.caller_saved [i];
				if (regToVar [(int)candidate] == -1) {
					return candidate;
				}
			}

			for (int i = 0; i < CallConv.callee_saved.Length; ++i) {
				Register candidate = CallConv.callee_saved [i];
				if (regToVar [(int)candidate] == -1) {
					callee_saved.Add (candidate);
					return candidate;
				}
			}

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
			Register reg = varToReg [vreg];
			if (reg == Register.None || reg == Register.Dead)
				throw new Exception ($"Can't assign bad reg to ins {reg}");
			return MaskReg (reg);
		}

		public void Move (Ins ins, int to, int from) {
			// if (varToReg [to] == Register.None)
				// throw new Exception ($"Dead var or bug? to {to} {varToReg[to]}");

			//this is a dead store, reduce the live range of from
			if (varToReg [to] == Register.None) {
				if (varToReg [from] == Register.None)
					Assign (from, FindReg ());
				ins.Dest = MaskReg (Register.RAX);
				ins.R0 = Conv (from);
				return;
			}
			//If this is the last usage of $from, we can treat this as a rename
			if (varToReg [from] == Register.None)
				Assign (from, varToReg [to]);

			ins.Dest = Conv (to);
			ins.R0 = Conv (from);

			varToReg [to] = Register.Dead;
		}

		public void Def (Ins ins, int vreg) {
			ins.Dest = Conv (vreg);
			//This just kills the reg
			regToVar [(int)varToReg [vreg]] = -1;
			varToReg [vreg] = Register.Dead;
		}

		public void BinOp (Ins ins, int dest, int r0, int r1) {
			//Binop follows x86 rules of r0 getting clobbered.
			if (varToReg [dest] == Register.None)
				throw new Exception ($"Dead var or bug? dest: {varToReg [dest]}");

			if (varToReg [r0] == Register.None)
				Assign (r0, varToReg [dest]);

			if (varToReg [r1] == Register.None)
				Alloc (r1);

			ins.Dest = Conv (dest);
			ins.R0 = Conv (r0);
			ins.R1 = Conv (r1);

			varToReg [dest] = Register.Dead;
		}

		public void CondBranch (Ins ins, int r0, int r1, CallInfo[] infos) {
			for (int j = 0; j < infos.Length; ++j)
				this.CallInfo (infos [j]);
			Alloc (r0);
			Alloc (r1);
			ins.R0 = Conv (r0);
			ins.R1 = Conv (r1);
		}

		public void DirectBranch (Ins ins, CallInfo[] infos) {
			for (int j = 0; j < infos.Length; ++j)
				this.CallInfo (infos [j]);
		}

		public void SetRet (Ins ins, int vreg) {
			if (varToReg [vreg].Valid ())
				throw new Exception ("SetReg MUST be the last use of the vreg on its BB");

			Register reg = CallConv.ReturnReg;
			if (regToVar [(int)reg] >= 0)
				throw new Exception ("For some reason, someone is already using SetReg's register. Fix your IR!");

			Assign (vreg, reg);
			ins.R0 = Conv (vreg);
		}

		public void LoadArg (Ins ins, int dest, int position) {
			Register reg = CallConv.RegForArg (position);
			if (varToReg [dest] != reg) {
				Console.WriteLine ($"need to do shit. I want {reg} but have {varToReg [dest]}");
				if (regToVar [(int)reg] >= 0)
					throw new Exception ($"the var we want is fucked {regToVar [(int)reg]}");
				ins.Op = Ops.Mov;
				ins.Dest = MaskReg (varToReg [dest]);
				ins.R0 = MaskReg (reg);
			}
			// throw new Exception ("whatevs");
		}

		void CallInfo (CallInfo info) {
			var repairing = new List<Tuple<Register,Register>> ();

			if (info.Target.InVarsAlloc == null) { //This happens the loop tail-> loop head edge.
				for (int i = 0; i < info.Args.Count; ++i)
					AllocIfNeeded (info.Args [i]);
				info.Target.NeedRepairing = true;
				info.NeedRepairing = true;
			} else {
				for (int i = 0; i < info.Args.Count; ++i) {
					Console.WriteLine ($"CI[{i}] {info.Args[i].V2S()} {info.Target.InVarsAlloc [i]}");
					Use (info.Args [i], info.Target.InVarsAlloc [i], repairing);
				}
			}

			for (int i = 0; i < info.Args.Count; ++i)
				info.Args [i] = Conv (info.Args [i]);

			if (repairing.Count > 0) {
				EmitRepairCode (info.Target, repairing);
			}
		}

		static void EmitRepairCode (BasicBlock bb, List<Tuple<Register,Register>> repairing) {
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
			bb.Prepend (new Ins (Ops.Mov) {
				Dest = MaskReg (repairing [0].Item1),
				R0 = MaskReg (repairing [0].Item2)
			});

			for (int i = 0; i < bb.InVarsAlloc.Count; ++i) {
				if (bb.InVarsAlloc [i] == repairing [0].Item1) {
					bb.InVarsAlloc [i] = repairing [0].Item2;
					break;
				}
			}
		}

		Register AllocIfNeeded (int vreg) {
			if (varToReg [vreg] != Register.None)
				return varToReg [vreg];
			return Alloc (vreg);
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
			var repairing = new List<Tuple<Register,Register>> ();
			for (int i = 0; i < info.Args.Count; ++i) {
				Register source = info.Args [i].UnmaskReg ();
				if (bb.InVarsAlloc [i] != source)
					repairing.Add (Tuple.Create (source, bb.InVarsAlloc [i]));
			}
			EmitRepairCode (bb, repairing);
		}

		public void Finish () {
			bb.InVarsAlloc = new List<Register> ();
			for (int i = 0; i < bb.InVars.Count; ++i) {
				Register reg = varToReg [i];
				if (reg == Register.None || reg == Register.Dead)
					throw new Exception ("WTF");
				bb.InVarsAlloc.Add (reg);
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

		}
		public string State {
			get {
				var s = string.Join (",", regToVar.Where (r => r != -1).Select (v => $"{v} => {varToReg [v]}"));

				return $"RA ({s})";
			}
		}
	}

	public class CallInfo {
		public List<int> Args { get; set; }
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
			return $"(BB{Target.Number}, {vars})";
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
			default:
				return $"{Op} {DStr} <= {R0Str} {R1Str}";
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
	public List<Register> InVarsAlloc { get; set; }

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
		if (InVarsAlloc != null) {
			var regset = string.Join (",", InVarsAlloc.Select (r => r.ToString ()));
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
				case Opcode.Ldloc0:
					AddUse (current.InVars, current.DefVars, 1);
					break;
				case Opcode.Ldloc1:
					AddUse (current.InVars, current.DefVars, 2);
					break;
				case Opcode.Ldloc2:
					AddUse (current.InVars, current.DefVars, 3);
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
			case Opcode.Ldloc0:
				s.LoadVar (varTable [1]);
				break;
			case Opcode.Ldloc1:
				s.LoadVar (varTable [2]);
				break;
			case Opcode.Ldloc2:
				s.LoadVar (varTable [3]);
				break;
			case Opcode.Ldarg0:
				s.LoadVar (varTable [-1]);
				break;
			case Opcode.Ldarg1:
				s.LoadVar (varTable [-2]);
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
		ra.Finish ();
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
		if (callee_saved.Count > 0) {
			Console.WriteLine ($"\tsubq 0x${callee_saved.Count * 8:X}, %rsp");
			int idx = 8;
			foreach (var reg in callee_saved) {
				Console.WriteLine ($"\tmovq ${reg.ToString ().ToLower ()}, -0x{idx:X}(%rbp)");
				idx += 8;
			}
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
					break;
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