using System;
using System.Collections.Generic;
using SimpleJit.CIL;
using SimpleJit.Metadata;
using System.Linq;

namespace SimpleJit.Compiler {

enum StackValueType {
	Int,
	Variable
}
internal struct StackValue {
	StackValueType type;
	int value; //Int -> constant, Variable -> var number

	internal static StackValue Int (int v) {
		return new StackValue () {
			type = StackValueType.Int,
			value = v
		};
	}
}

internal class EvalStack  {
	Stack <Ins> stack = new Stack <Ins> ();
	Stack <StackValue> stack2 = new Stack <StackValue> ();
	BasicBlock bb;

	public EvalStack (BasicBlock bb) {
		this.bb = bb;
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

	public void PushInt (int c) {
		Console.WriteLine ("PushInt {0}", c);

		var i = new Ins (Ops.IConst) {
			Dest = bb.NextReg (),
			Const0 = c,
		};
		stack.Push (i);
		bb.Append (i);

		// stack2.Push (StackValue.Int (c));
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

public class FrontEndTranslator {
	MethodData method;
	BasicBlock bb;

	public FrontEndTranslator (MethodData method, BasicBlock bb) {
		this.method = method;
		this.bb = bb;
	}
	
	public void Translate () {
		Console.WriteLine ("Emitting body of BB{0}", bb.Number);
		var varTable = new Dictionary <int, int> ();
		Console.WriteLine ("initial var map:");
		foreach (var v in bb.InVars) {
			varTable [v] = bb.NextReg ();
			Console.WriteLine ($"\t{v} == {varTable [v]}");
		}

		var it = bb.GetILIterator ();
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
			case Opcode.LdcI4_1:
			case Opcode.LdcI4_2:
			case Opcode.LdcI4_3:
			case Opcode.LdcI4_4:
			case Opcode.LdcI4_5:
				s.PushInt ((int)it.Opcode - (int)Opcode.LdcI4_0);
				break;
			case Opcode.LdcI4S:
				s.PushInt (it.DecodeParamI ());
				break;

			case Opcode.Stloc0:
			case Opcode.Stloc1:
			case Opcode.Stloc2:
			case Opcode.Stloc3:
				varTable [1 + ((int)it.Opcode - (int)Opcode.Stloc0)] = s.StoreVar ();
				break;
			case Opcode.StlocS:
				varTable [1 + it.DecodeParamI ()] = s.StoreVar ();
				break;

			case Opcode.Ldloc0:
			case Opcode.Ldloc1:
			case Opcode.Ldloc2:
			case Opcode.Ldloc3:
				s.LoadVar (varTable [1 + ((int)it.Opcode - (int)Opcode.Ldloc0)]);
				break;
			case Opcode.LdlocS:
				s.LoadVar (varTable [1 + it.DecodeParamI ()]);
				break;

			case Opcode.Ldarg0:
			case Opcode.Ldarg1:
			case Opcode.Ldarg2:
			case Opcode.Ldarg3:
				s.LoadVar (varTable [-1 - ((int)it.Opcode - (int)Opcode.Ldarg0)]);
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
}
}
