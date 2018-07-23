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

namespace SimpleJit.Compiler {
	internal static class IntExtensions {
		internal static Register UnmaskReg (this int v) {
			return (Register)(-v - 1);
		}

		internal static string V2S (this int vreg) {
			if (vreg >= 0)
				return "R" + vreg;
			return vreg.UnmaskReg ().ToString ();
		}
	}

	public class CallInfo {
		public List<int> Args { get; set; }
		public List<VarState> AllocResult { get; set; }
		public List<Ins> CpropValues { get; set; } //XXX this is fugly :D

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

	public partial class Ins {
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
		public CallInfo[] CallInfos { get; set; } //branches
		public int[] CallVars { get; set; } //calls
		public MethodData Method {get; set; }//call target


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

		string CallArgsStr {
			get {
				return string.Join (",", CallVars.Select (_ => _.V2S ()));
			}
		}

		public void SetNext (Ins i) {
			if (i != null)
				i.Prev = this;
			this.Next = i;
		}

		public void Append (Ins i) {
			if (this.Next != null) {
				Ins last = i;
				while (last.Next != null)
					last = last.Next;
				this.Next.Prev = last;
				last.Next = this.Next;
			}

			i.Prev = this;
			this.Next = i;
		}

		public void Prepend (Ins i) {
			if (i.Prev != null)
				throw new Exception ("TODO multi op prepend");
			if (this.Prev != null) {
				this.Prev.Next = i;
				i.Prev = this.Prev;
			}

			i.Next = this;
			this.Prev = i;
		}

		public void ReplaceWith (Ins r) {
			r.Prev = Prev;
			r.Next = Next;
			if (Prev != null)
				Prev.Next = r;
			if (Next != null)
				Next.Prev = r;
		}

		public void MakeNop () {
			Op = Ops.Nop;
			Dest = R0 = R1 = -100;
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
	internal RegPrefs[] RegPrefs { get; set; }

	public int StackArgs { get; set; }

	Compiler compiler;
	Ins first, last;
	int reg;

	public BasicBlock (Compiler compiler) {
		Number = compiler.bb_number++;
		this.compiler = compiler;
	}

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

	public IlIterator GetILIterator () {
		return new IlIterator (compiler.body.Body, this.Start, this.End);
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
				// Console.WriteLine (c);
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

	public CallInfo InfoFor (BasicBlock bb) {
		CallInfo[] infos = LastIns.CallInfos;
		for (int j = 0; j < infos.Length; ++j) {
			if (infos [j].Target == bb)
				return infos [j];
		}
		return null;
	}

	public void ReplaceWith (Ins orig, Ins replace) {
		orig.ReplaceWith (replace);
		if (first == orig)
			first = replace;
		if (last == orig)
			last = replace;
	}
}

public class Compiler {
	MethodData method;
	internal MethodBody body;
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
					if (next != null)
						current.UnlinkTo (next);
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

	int LocalToDic (int local) {
		return 1 + method.Signature.ParamCount + local;
	}

	int ArgToDic (int arg) {
		return 1 + arg;
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

			var it = current.GetILIterator ();

			while (it.MoveNext ()) {
				Console.WriteLine ("\t[{0}]:{1}", it.Index, it.Mnemonic);
				switch (it.Opcode) {
				case Opcode.Stloc0:
				case Opcode.Stloc1:
				case Opcode.Stloc2:
				case Opcode.Stloc3:
					AddDef (current.InVars, current.DefVars, LocalToDic((int)it.Opcode - (int)Opcode.Stloc0));
					break;
				case Opcode.StlocS:
					AddDef (current.InVars, current.DefVars, LocalToDic (it.DecodeParamI ()));
					break;
				case Opcode.Ldloc0:
				case Opcode.Ldloc1:
				case Opcode.Ldloc2:
				case Opcode.Ldloc3:
					AddUse (current.InVars, current.DefVars, LocalToDic ((int)it.Opcode - (int)Opcode.Ldloc0));
					break;
				case Opcode.LdlocS:
					AddUse (current.InVars, current.DefVars, LocalToDic (it.DecodeParamI ()));
					break;

				case Opcode.Ldarg0:
				case Opcode.Ldarg1:
				case Opcode.Ldarg2:
				case Opcode.Ldarg3:
					AddUse (current.InVars, current.DefVars, ArgToDic ((int)it.Opcode - (int)Opcode.Ldarg0));
					break;
				case Opcode.StargS:
					AddDef (current.InVars, current.DefVars, ArgToDic (it.DecodeParamI ()));
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
			new FrontEndTranslator (method, current).Translate ();
		}

		DumpBB ("EmitIR done");
	}

	SortedSet<Register> callee_saved = new SortedSet<Register> ();
	int spillAreaUsed = 0;

	void RegAlloc (BasicBlock bb) {
		Console.WriteLine ("Allocating BB{0}", bb.Number);
		var ra = new RegAllocState (bb, callee_saved);
		for (Ins ins = bb.LastIns, prev = null; ins != null; ins = prev) {
			Console.WriteLine ($"Before {ins.ToString ()}");
			prev = ins.Prev;
			//Alloc ops are: IConst, Move, GeneralInsAlloc, SetRet, Call, LoadArg
			switch (ins.GetRegAllocCat ()) {
			case RegAllocCat.D:
				ra.IConst (ins);
				break;

			case RegAllocCat.Move:
				ra.Move (ins);
				break;

			case RegAllocCat.DA1:
				ra.GeneralInsAlloc (ins, RU.Yes, RU.Yes, RU.No);
				break;

			case RegAllocCat.DA2:
				ra.GeneralInsAlloc (ins, RU.Yes, RU.Clobber, RU.Yes);
				break;

			case RegAllocCat.DA1Clob:
				ra.GeneralInsAlloc (ins, RU.Yes, RU.Clobber, RU.No);
				break;

			case RegAllocCat.A2:
				ra.GeneralInsAlloc (ins, RU.No, RU.Yes, RU.Yes);
				break;

			case RegAllocCat.BR1:
				ra.GeneralInsAlloc (ins, RU.No, RU.No, RU.No);
				break;

			case RegAllocCat.BR2:
				ra.GeneralInsAlloc (ins, RU.No, RU.No, RU.No);
				break;

			case RegAllocCat.A1:
				ra.GeneralInsAlloc (ins, RU.No, RU.Yes, RU.No);
				break;

			case RegAllocCat.A1R:
				ra.SetRet (ins);
				break;

			case RegAllocCat.ICall:
				ra.Call (ins);
				break;

			case RegAllocCat.ARG:
				ra.LoadArg (ins, ins.Dest, ins.Const0);
				break;

			case RegAllocCat.VCall:
				throw new Exception ("Can't regalloc a void call :(");
			default:
				throw new Exception ($"Invalid regalloc category ({ins.GetRegAllocCat ()}) for ins ({ins})");
			}

			Console.WriteLine ($"After {ins.ToString ()}\n\t{ra.State}");
		}
		spillAreaUsed = Math.Max (spillAreaUsed, ra.Finish ());
		Console.WriteLine ("AFTER RA:\n{0}", bb);
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

		var pass = new RegPreferencesPass (this);
		pass.Run ();

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
		case Ops.Bg: return "jg";
		case Ops.Bge: return "jge";
		case Ops.Bne: return "jne";
		case Ops.Beq: return "je";
		default: throw new Exception ($"Not a branch op {op}");
		}
	}

	string IRegName (int vreg) {
		var reg = vreg.UnmaskReg ();
		return reg.GetIReg ().ToString ().ToLower ();
	}

	void CodeGen (StreamWriter asm) {
		Console.WriteLine ("--- CODEGEN");

		//hardcoded prologue
		asm.WriteLine ($".globl _{method.Name}");
		asm.WriteLine (".align	4");

		asm.WriteLine ($"_{method.Name}:");
		asm.WriteLine ("\tpushq %rbp");
		asm.WriteLine ("\tmovq %rsp, %rbp");

		//Emit save
		int stack_space = (callee_saved.Count + spillAreaUsed) * 8;
		int spillOffset = 8;
		if (stack_space > 0) {
			asm.WriteLine ($"\tsubq 0x${stack_space:X}, %rsp");
			int idx = 8;
			foreach (var reg in callee_saved) {
				asm.WriteLine ($"\tmovq ${reg.ToString ().ToLower ()}, -0x{idx:X}(%rbp)");
				idx += 8;
			}
			spillOffset = idx;
		}

		for (var bb = first_bb; bb != null; bb = bb.NextInOrder) {
			asm.WriteLine ($"_{method.Name}_BB{bb.Number}:");

			for (Ins ins = bb.FirstIns; ins != null; ins = ins.Next) {
				switch (ins.Op) {
				case Ops.Mov:
					if (ins.Dest != ins.R0)
						asm.WriteLine ($"\tmovq %{ins.R0.V2S().ToLower ()}, %{ins.Dest.V2S().ToLower ()}");
					break;
				case Ops.IConst: {
					if (ins.Const0 == 0) {
						asm.WriteLine ($"\txorq %{ins.Dest.V2S().ToLower ()}, %{ins.Dest.V2S().ToLower ()}");
					} else {
						var str = ins.Const0.ToString ("X");
						asm.WriteLine ($"\tmovq $0x{str}, %{ins.Dest.V2S().ToLower ()}");
					}
					break;
				}
				case Ops.Ble:
				case Ops.Blt:
				case Ops.Bg:
				case Ops.Bge:
				case Ops.Bne:
				case Ops.Beq: {
					var mi = BranchOpToJmp (ins.Op);
					asm.WriteLine ($"\t{mi} _{method.Name}_BB{ins.CallInfos[0].Target.Number}");
					break;
				} case Ops.Br:
					if (ins.CallInfos[0].Target != bb.NextInOrder)
						asm.WriteLine ($"\tjmp _{method.Name}_BB{ins.CallInfos[0].Target.Number}");
					break;
				case Ops.Add:
					if (ins.Dest != ins.R0)
						throw new Exception ("Bad binop encoding!");
					asm.WriteLine ($"\taddl %{IRegName (ins.R1)}, %{IRegName (ins.Dest)}");
					break;
				case Ops.AddI:
					if (ins.Dest != ins.R0)
						throw new Exception ("Bad binop encoding!");
					if (ins.Const0 == 1)
						asm.WriteLine ($"\tincl %{IRegName (ins.Dest)}");
					else
						asm.WriteLine ($"\taddl $0x{ins.Const0:X}, %{IRegName (ins.Dest)}");
					break;
				case Ops.Mul:
					if (ins.Dest != ins.R0)
						throw new Exception ("Bad binop encoding!");
					asm.WriteLine ($"\timull %{IRegName (ins.R1)}, %{IRegName(ins.Dest)}");
					break;
				case Ops.MulI:
					asm.WriteLine ($"\timull $0x{ins.Const0:X}, %{IRegName(ins.R0)}, %{IRegName(ins.Dest)}");
					break;
				case Ops.SetRet:
				case Ops.Nop:
					break;
				case Ops.SpillVar: {
					int offset = spillOffset + ins.Const0 * 8;
					asm.WriteLine ($"\tmovq ${ins.R0.V2S().ToLower ()}, -0x{(offset):X}(%rbp)");
					break;
				}
				case Ops.SpillConst: {
					int offset = spillOffset + ins.Const0 * 8;
					var str = ins.Const1.ToString ("X");
					asm.WriteLine ($"\tmovq $0x{str}, -0x{(offset):X}(%rbp)");
					break;
				}
				case Ops.FillVar: {
					int offset = spillOffset + ins.Const0 * 8;
					asm.WriteLine ($"\tmovq -0x{offset:X}(%rbp), ${ins.Dest.V2S().ToString ().ToLower ()}");
					break;
				}
				case Ops.CmpI: {
					var str = ins.Const0.ToString ("X");
					asm.WriteLine ($"\tcmpq $0x{str}, %{ins.R0.V2S().ToLower ()}");
					break;
				}
				case Ops.Cmp: {
					asm.WriteLine ($"\tcmpq %{ins.R1.V2S().ToLower ()}, %{ins.R0.V2S().ToLower ()}");
					break;
				}
				case Ops.Call: {
					asm.WriteLine ($"\tleaq _{ins.Method.Name}(%rip), %r11");
					asm.WriteLine ($"\tcallq *%r11");
					break;
				}
				case Ops.Swap: {
					asm.WriteLine ($"\txchgl %{ins.R0.V2S().ToLower ()}, %{ins.R1.V2S().ToLower ()}");
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
				asm.WriteLine ($"\tmovq -0x{idx:X}(%rbp), ${reg.ToString ().ToLower ()}");
				idx += 8;
			}
		}

		asm.WriteLine ("\tleave");
		asm.WriteLine ("\tretq");

	}
	public void Run (StreamWriter asm) {
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
		var pass = new CpropPass (this);
		pass.Run ();

		//step 4, insel, scheduling

		//step 5, spill & reg alloc
		RegAlloc ();

		//step 6, codegen
		CodeGen (asm);
	}

	public void Dump (string reason) {
		Console.WriteLine (reason);
		for (var bb = first_bb; bb != null; bb = bb.NextInOrder)
			Console.WriteLine (bb);
	}
	//XXX fix this to respect predecessor ordering
	public void ForwardPropPass (Action<BasicBlock> cb) {
		BasicBlock bb;
		var q = new List <BasicBlock> (); //XXX use a queue

		//first we do a forward pass to propagate returns and in arg regs
		for (bb = this.first_bb; bb != null; bb = bb.NextInOrder)
			bb.Enqueued = bb.Done = false;

		q.Add (this.first_bb);

		while (q.Count > 0) {
			var current = q [0];
			q.RemoveAt (0);
			current.Enqueued = false;
			current.Done = true;

			cb (current);

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