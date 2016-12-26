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

	public enum Ops {
		IConst,
		Mov,
		SetRet,
		Add,
		Ble,
	}

	public class CallInfo {
		public List<int> Args { get; set; }
		public BasicBlock Target { get; set; }

		public CallInfo (Dictionary <int, int> varTable, BasicBlock target) {
			Args = new List<int> ();
			Console.WriteLine ("Computing translation table to BB{0}", target.Number);
			foreach (var v in target.InVars) {
				Console.WriteLine ("\tlooking up var {0}", v);
				Args.Add (varTable [v]);
			}
			Target = target;
		}
	}

	public class Ins {
		public Ins (Ops op) {
			this.Op = op;
		}
		public Ops Op { get; private set; }
		public int Dest { get; set; }
		public int R0 { get; set; }
		public int R1 { get; set; }
		public int Const0 { get; set; }
		public CallInfo[] CallInfos { get; set; }

		public Ins Prev { get; private set; }
		public Ins Next { get; private set; }

		public override string ToString () {
			switch (Op) {
			case Ops.IConst:
				return $"{Op} R{Dest} <= {Const0}";
			case Ops.Mov:
				return $"{Op} R{Dest} <= R{R0}";
			case Ops.SetRet:
				return $"{Op} R{R0}";
			case Ops.Ble:
				return $"{Op} R{R0} R{R1} ({CallInfos[0]}) ({CallInfos[1]})";
			default:
				return $"{Op} R{Dest} <= R{R0} R{R1}";
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
	public BasicBlock NextInOrder { get; private set; }
	List <BasicBlock> from = new List <BasicBlock> ();
	List <BasicBlock> to = new List <BasicBlock> ();

	internal HashSet<int> InVars = new HashSet<int> (); 
	// internal HashSet<int> OutVars = new HashSet<int> ();
	internal HashSet<int> DefVars = new HashSet<int> ();

	Ins first, last;
	int reg;

	public int NextReg () {
		return reg++;
	}

	public void Append (Ins ins) {
		if (first == null) {
			first = last = ins;
		} else {
			last.SetNext (ins);
			last = ins;
		}
	}
	public BasicBlock (Compiler c) {
		Number = c.bb_number++;
	}

	public IReadOnlyList <BasicBlock> To { get { return to; } }
	public IReadOnlyList <BasicBlock> From { get { return from; } }
	public bool Enqueued { get; set; }
	public bool Done { get; set; }

	public override string ToString () {
		var fromStr = string.Join (",", from.Select (bb => bb.Number.ToString ()));
		var toStr = string.Join (",", to.Select (bb => bb.Number.ToString ()));
		var inVars = string.Join (",", InVars);
		// var outVars = string.Join (",", OutVars);
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
		return $"BB ({Number}) [0x{Start:X} - 0x{End:X}] FROM ({fromStr}) TO ({toStr}) IN-VARS ({inVars}) DEFS ({defVars}) {body}";
	}

	public BasicBlock SplitAt (Compiler c, int offset, bool link) {
		// Console.WriteLine ($"splitting ({Number}) at 0x{offset:X}");
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
	internal BasicBlock first_bb;
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
					current = next;
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
		DumpBB ("BB Formation");
	}

	static void AddUse (HashSet<int> inVars, HashSet<int> defs, int var) {
		if (!defs.Contains (var)) {
			Console.WriteLine ($"\t\tFound use of {var}");
			inVars.Add (var);
		}
	}

	static void AddDef (HashSet<int> inVars, HashSet<int> defs, int var) {
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
				case Opcode.Ldloc0:
					AddUse (current.InVars, current.DefVars, 1);
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

				}
			}
			//Queue leaf nodes as they are all ready
			if (current.To.Count == 0) {
				list.Add (current);
				current.Enqueued = true;
			}
		}

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
			//XXX figure out proper invalidation rules
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
			stack.Push (i);
			bb.Append (i);
		}

		public void EmitBranch () { }

		public void SetRet () {
			var r0 = stack.Pop ().Dest;
			var i = new Ins (Ops.SetRet) {
				R0 = r0,
			};
			stack.Push (i);
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
		while (it.MoveNext ()) {
			switch (it.Opcode) {
			case Opcode.Add:
				s.PushBinOp (Opcode.Add);
				break;
			case Opcode.LdcI4_0:
				s.PushInt (0);
				break;
			case Opcode.LdcI4_2:
				s.PushInt (2);
				break;
			case Opcode.Stloc0:
				varTable [1] = s.StoreVar ();
				break;
			case Opcode.Ldloc0:
				s.LoadVar (varTable [1]);
				break;
			case Opcode.Ldarg0:
				s.LoadVar (varTable [-1]);
				break;
			case Opcode.Ldarg1:
				s.LoadVar (varTable [-2]);
				break;
			case Opcode.Ble:
				Console.WriteLine ("varTable before cond:");
				foreach (var kv in varTable)
					Console.WriteLine ($"\t{kv.Key} -> {kv.Value}");

				var infos = new CallInfo [2];
				infos [0] = new CallInfo (varTable, bb.To [0]);
				infos [0] = new CallInfo (varTable, bb.To [1]);

				s.EmitCondBranch (Opcode.Ble, infos);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				break;
			case Opcode.Br:
				s.EmitBranch ();
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				break;
			case Opcode.Ret:
				s.SetRet (); //XXX check signatune to see whether a store ret is needed
				s.EmitBranch ();
				if (it.HasNext)
					throw new Exception ("Ret MUST be last op in a BB");
				break;

			default:
				throw new Exception ($"Cannot emit {it.Mnemonic}");
			}
		}
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

		//set 1, BB formation
		ComputeBasicBlocks ();

		//set 2, emit BBs in reverse order
		EmitBasicBlockBodies ();
	}
}

}