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


public class BasicBlock {
	public int Start { get; set; }
	public int End { get; set; }
	public int Number { get; private set; }
	public BasicBlock NextInOrder { get; private set; }
	List <BasicBlock> from = new List <BasicBlock> ();
	List <BasicBlock> to = new List <BasicBlock> ();

	internal HashSet<int> InVars = new HashSet<int> (); 
	internal HashSet<int> OutVars = new HashSet<int> ();
	internal HashSet<int> DefVars = new HashSet<int> ();

	public BasicBlock (Compiler c) {
		Number = c.bb_number++;
	}

	public IReadOnlyList <BasicBlock> To { get { return to; } }
	public IReadOnlyList <BasicBlock> From { get { return from; } }
	public bool Visited { get; set; }
	public bool Done { get; set; }

	public override string ToString () {
		var fromStr = string.Join (",", from.Select (bb => bb.Number.ToString ()));
		var toStr = string.Join (",", to.Select (bb => bb.Number.ToString ()));
		var inVars = string.Join (",", InVars);
		var outVars = string.Join (",", OutVars);
		var defVars = string.Join (",", DefVars);
		
		return $"BB ({Number}) [0x{Start:X} - 0x{End:X}] FROM ({fromStr}) TO ({toStr}) IN-VARS ({inVars}) OUT-VARS ({outVars}) DEFS ({defVars})";
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

	public void DumpBB () {
		BasicBlock current;
		Console.WriteLine ("===BBs:");
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
				// DumpBB ();
				break;
			}

			case OpcodeFlags.CondBranch: {
				var target_offset = it.NextIndex + it.DecodeParamI ();
				var next = current.SplitAt (this, it.NextIndex, link: true);
				var target = first_bb.Find (target_offset).SplitAt (this, target_offset, link: true);
				current.LinkTo (target);
				current = next;
				// DumpBB ();
				break;
			}
			case OpcodeFlags.Return: {
				if (it.HasNext) {
					var next = current.SplitAt (this, it.NextIndex, link: false);
					current = next;
					// DumpBB ();
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
		DumpBB ();
	}

	static void AddUse (HashSet<int> uses, HashSet<int> defs, int var) {
		if (!defs.Contains (var)) {
			Console.WriteLine ($"\t\tFound use of {var}");
			uses.Add (var);
		}
	}

	static void AddDef (HashSet<int> uses, HashSet<int> defs, int var) {
		// if (!uses.Contains (var)) {
		Console.WriteLine ($"\t\tFound def of {var}");
			defs.Add (var);
		// }
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

			HashSet<int> uses = new HashSet<int> (); 
			HashSet<int> defs = new HashSet<int> ();

			while (it.MoveNext ()) {
				Console.WriteLine ("\t[{0}]:{1}", it.Index, it.Mnemonic);
				switch (it.Opcode) {
				case Opcode.Stloc0:
					AddDef (uses, defs, 1);
					break;
				case Opcode.Ldloc0:
					AddUse (uses, defs, 1);
					break;
				case Opcode.StargS:
					AddDef (uses, defs, -1 - it.DecodeParamI ());
					break;
				case Opcode.Ldarg0:
					AddUse (uses, defs, -1);
					break;
				case Opcode.Ldarg1:
					AddUse (uses, defs, -2);
					break;

				}
			}
			current.InVars.UnionWith (uses);
			current.DefVars.UnionWith (defs);

			//Queue leaf nodes as they are all ready
			if (current.To.Count == 0) {
				list.Add (current);
				current.Visited = true;
			}
		}
		DumpBB ();

		while (list.Count > 0) {
			current = list [0];
			list.RemoveAt (0);

			int inC = current.InVars.Count;
			int outC = current.OutVars.Count;
			foreach (var next in current.To)
				current.OutVars.UnionWith (next.InVars);
			foreach (var prev in current.From)
				current.InVars.UnionWith (prev.OutVars);

			//XXX figure out proper invalidation rules
			//bool all = inC != current.InVars.Count || outC != current.OutVars.Count;
			
			foreach (var prev in current.From) {
				if (!prev.Visited) {
					list.Add (prev);
					prev.Visited = true;
				}
			}
		}
		//compute initial candidates, emit in reverse dominator order
		DumpBB ();
	}
	void OldEmitBasicBlockBodies () {
		var list = new List<BasicBlock> (); //FIXME use a queue collection

		Console.WriteLine ("=== BB code emit");


		BasicBlock current;
		for (current = first_bb; current != null; current = current.NextInOrder) {
			if (current.To.Count == 0) {
				list.Add (current);
				current.Visited = true;
			}
		}

		while (list.Count > 0) {
			current = list [0];
			list.RemoveAt (0);

			Console.WriteLine ($"processing {current}");
			//zero has no meaning locs are positive, args are negative
			HashSet<int> uses = new HashSet<int> (); 
			HashSet<int> defs = new HashSet<int> ();

			var it = GetILIterator (current);
			while (it.MoveNext ()) {
				Console.WriteLine ("\t[{0}]:{1}", it.Index, it.Mnemonic);
				switch (it.Opcode) {
				case Opcode.Stloc0:
					AddDef (uses, defs, 1);
					break;
				case Opcode.Ldloc0:
					AddUse (uses, defs, 1);
					break;
				case Opcode.StargS:
					AddDef (uses, defs, -1 - it.DecodeParamI ());
					break;
				case Opcode.Ldarg0:
					AddUse (uses, defs, -1);
					break;
				}
			}

			current.InVars.UnionWith (uses);

			foreach (var next in current.To) {
				foreach (var v in next.InVars) {
					if (!defs.Contains (v))
						current.InVars.Add (v);
				}
			}

			//current.OutVars.UnionWith (defs);


			Console.WriteLine ("\tUses ({0}) defs ({1})", string.Join(",", uses), string.Join (",", defs));
			foreach (var prev in current.From) {
				if (!prev.Visited) {
					prev.Visited = true;
					list.Add (prev);
				}
			}
		}
		DumpBB ();
	}

	void ResetVisited () {
		BasicBlock current;

		for (current = first_bb; current != null; current = current.NextInOrder)
			current.Visited = false;
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