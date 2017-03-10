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
	internal StackValueType type;
	internal int value; //Int -> constant, Variable -> var number

	internal static StackValue Int (int v) {
		return new StackValue () {
			type = StackValueType.Int,
			value = v
		};
	}

	internal static StackValue Var (int v) {
		return new StackValue () {
			type = StackValueType.Variable,
			value = v
		};
	}

	internal bool IsConst { get { return type == StackValueType.Int; } }
	internal bool IsVar { get { return type == StackValueType.Variable; } }

	public override string ToString () {
		if (IsConst)
			return $"(C {value})";
		if (IsVar)
			return $"(V R{value})";
		return "(UNK)";
	}
}

internal class EvalStack {
	BasicBlock bb;

	Stack <StackValue> stack2 = new Stack <StackValue> ();
	Dictionary <int, int> varToVreg = new Dictionary <int, int> ();
	

	static int StackVar (int v) {
		return -1000 - v;
	}

	static bool IsStackVar (int v) {
		return v <= -1000;
	}

	public EvalStack (BasicBlock bb) {
		this.bb = bb;
		Console.WriteLine ("initial var map:");
		foreach (var v in bb.InVars) {
			int reg = bb.NextReg ();
			if (IsStackVar (v)) {
				stack2.Push (StackValue.Var (reg));
				Console.WriteLine ($"\tpushed {reg}");
			} else {
				varToVreg [v] = reg;
				Console.WriteLine ($"\t{v} == {reg}");
			}
		}
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
		case Opcode.Brfalse:
		case Opcode.BrfalseS:
			return Ops.Beq; //we emil a compare to zero
		case Opcode.Brtrue:
		case Opcode.BrtrueS:
			return Ops.Bne; //we emil a compare to zero
		default: throw new Exception ($"{op} is not a condop");
		}
	}

	static Ops InvertCond (Ops op) {
		switch (op) {
		case Ops.Ble: return Ops.Bg;
		case Ops.Blt: return Ops.Bge;
		case Ops.Bg: return Ops.Ble;
		case Ops.Bge: return Ops.Blt;
		case Ops.Beq: return Ops.Bne;
		case Ops.Bne: return Ops.Beq;
		default: throw new Exception ($"{op} is not a condop");
		}
	}

	public void PushInt (int c) {
		Console.WriteLine ("PushInt {0}", c);
		stack2.Push (StackValue.Int (c));
	}


	public void StoreVar (int cilVar) {
		Console.WriteLine ("StoreVar {0}", cilVar);

		var val = stack2.Pop ();
		switch (val.type) {
		case StackValueType.Int: {
			int nextReg = bb.NextReg ();
			bb.Append (new Ins (Ops.IConst) {
				Dest = nextReg,
				Const0 = val.value,
			});
			varToVreg [cilVar] = nextReg;
			break;
		}
		case StackValueType.Variable:
			varToVreg [cilVar] = val.value;
			break;
		}
	}

	public void LoadVar (int cilVar) {
		Console.WriteLine ("LoadVar {0}", cilVar);
		stack2.Push (StackValue.Var (varToVreg [cilVar]));
	}

	public void PushBinOp (Opcode op) {
		Console.WriteLine ("BinOp {0}", op);

		var r1 = stack2.Pop ();
		var r0 = stack2.Pop ();

		if (op != Opcode.Add)
			throw new Exception ("Can only handle add for now");
		if (r0.IsConst && r1.IsConst) {
			stack2.Push (StackValue.Int (r1.value + r0.value));
		} else if (r0.IsConst || r1.IsConst) {
			int c;
			int vreg;
			int nextReg = bb.NextReg ();
			if (r0.IsConst) {
				c = r0.value;
				vreg = r1.value;
			} else {
				c = r1.value;
				vreg = r0.value;
			}
			bb.Append (new Ins (Ops.AddI) {
				Dest = nextReg,
				R0 = vreg,
				Const0 = c,
			});
			stack2.Push (StackValue.Var (nextReg));
		} else {
			int nextReg = bb.NextReg ();
			bb.Append (new Ins (Ops.Add) {
				Dest = nextReg,
				R0 = r0.value,
				R1 = r1.value
			});
			stack2.Push (StackValue.Var (nextReg));
		}
	}

	public void EmitCondBranch (Opcode cond, BasicBlock bb1, BasicBlock bb2) {
		Console.WriteLine ("CondBranch {0}", cond);

		Console.WriteLine ("varToVreg before cond:");
		foreach (var kv in varToVreg)
			Console.WriteLine ($"\t{kv.Key} -> {kv.Value}");

		var infos = new CallInfo [2];
		infos [0] = new CallInfo (varToVreg, bb1);
		infos [1] = new CallInfo (varToVreg, bb2);

		var r1 = stack2.Pop ();
		var r0 = stack2.Pop ();

		Ops branchOp;

		if (r0.IsConst && r1.IsConst) {
			throw new Exception ("CAN'T HANDLE Cond->FIXED branch");
		} else if (r0.IsConst || r1.IsConst) {
			Console.WriteLine ("we got {0} {1}", r0, r1);
			int c;
			int vreg;
			if (r0.IsConst) {
				c = r0.value;
				vreg = r1.value;
				branchOp = InvertCond (CilToCondOp (cond));
			} else {
				c = r1.value;
				vreg = r0.value;
				branchOp = CilToCondOp (cond);
			}
			bb.Append (new Ins (Ops.CmpI) {
				R0 = vreg,
				Const0 = c,
			});
		} else {
			branchOp = CilToCondOp (cond);
			bb.Append (new Ins (Ops.Cmp) {
				R0 = r0.value,
				R1 = r1.value
			});
		}

		bb.Append (new Ins (branchOp) {
			CallInfos = infos,
		});
	}

	public void EmitBoolBranch (Opcode cond, BasicBlock bb1, BasicBlock bb2) {
		Console.WriteLine ("BoolCondBranch {0}", cond);

		Console.WriteLine ("varToVreg before cond:");
		foreach (var kv in varToVreg)
			Console.WriteLine ($"\t{kv.Key} -> {kv.Value}");
		var r0 = stack2.Pop ();
		if (stack2.Count > 0)
			throw new Exception ("AHA!");

		var infos = new CallInfo [2];
		infos [0] = new CallInfo (varToVreg, bb1);
		infos [1] = new CallInfo (varToVreg, bb2);


		if (r0.IsConst)
			throw new Exception ("CAN'T HANDLE Cond->FIXED branch");

		bb.Append (new Ins (Ops.CmpI) {
			R0 = r0.value,
			Const0 = 0,
		});

		bb.Append (new Ins (CilToCondOp (cond)) {
			CallInfos = infos,
		});
	}

	int Flush (StackValue val) {
		if (val.IsVar)
			return val.value;
		int nextReg = bb.NextReg ();
		bb.Append (new Ins (Ops.IConst) {
			Dest = nextReg,
			Const0 = val.value,
		});
		return nextReg;
	}
	public void EmitBranch (BasicBlock to) {
		Console.WriteLine ("Branch BB{0}", to.Number);
		if (stack2.Count > 0) {
			if (to.FirstIns != null && to.StackArgs != stack2.Count)
				throw new Exception ($"Target BB{to.Number} expected {to.StackArgs} stack args but current BB passes {stack2.Count}!");
			if (to.StackArgs != stack2.Count) {
				to.StackArgs = stack2.Count;
				for (int i = 0; i < stack2.Count; ++i)
					to.InVars.Add (StackVar (i));
			}
			while (stack2.Count > 0) {
				var val = stack2.Pop ();
				varToVreg [StackVar (stack2.Count)] = Flush (val);
			}
		}

		CallInfo info = new CallInfo (varToVreg, to);

		var ins = new Ins (Ops.Br) {
			CallInfos = new CallInfo[] { info },
		};
		bb.Append (ins);
	}

	int PopVreg () {
		var val = stack2.Pop ();
		switch (val.type) {
		case StackValueType.Int: {
			int nextReg = bb.NextReg ();
			bb.Append (new Ins (Ops.IConst) {
				Dest = nextReg,
				Const0 = val.value,
			});
			return nextReg;
			break;
		}
		case StackValueType.Variable:
			return val.value;
			break;
		default:
			throw new Exception ($"Invalid stack type {val.type}");
		}
	}

	public void EmitCall (MethodData md) {
		var sig = md.Signature;
		Ins ins;
		if (sig.ReturnType != ClrType.Void) {
			int nextReg = bb.NextReg ();
			ins = new Ins (Ops.Call) {
				Dest = nextReg
			};
		} else {
			ins = new Ins (Ops.VoidCall);
		}

		ins.Method = md;
		ins.CallVars = new int [sig.ParamCount];
		for (int i = 0; i < sig.ParamCount; ++i) {
			ins.CallVars [sig.ParamCount - (i + 1)] = PopVreg ();
		}

		bb.Append (ins);
		if (sig.ReturnType != ClrType.Void)
			stack2.Push (StackValue.Var (ins.Dest));
	}
}

public class FrontEndTranslator {
	MethodData method;
	BasicBlock bb;
	int localOffset;

	public FrontEndTranslator (MethodData method, BasicBlock bb) {
		this.method = method;
		this.bb = bb;
		localOffset = 1 + method.Signature.ParamCount;
	}

	int LocalToDic (int local) {
		return localOffset + local;
	}

	int ArgToDic (int arg) {
		return 1 + arg;
	}

	public void Translate () {
		Console.WriteLine ("Emitting body of BB{0}", bb.Number);
		var varTable = new Dictionary <int, int> ();

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
			case Opcode.LdcI4_6:
			case Opcode.LdcI4_7:
			case Opcode.LdcI4_8:
				s.PushInt ((int)it.Opcode - (int)Opcode.LdcI4_0);
				break;
			case Opcode.LdcI4:
				s.PushInt (it.DecodeParamI ());
				break;
			case Opcode.LdcI4S:
				s.PushInt (it.DecodeParamI ());
				break;

			case Opcode.Stloc0:
			case Opcode.Stloc1:
			case Opcode.Stloc2:
			case Opcode.Stloc3:
				s.StoreVar (LocalToDic((int)it.Opcode - (int)Opcode.Stloc0));
				break;
			case Opcode.StlocS:
				s.StoreVar (LocalToDic (it.DecodeParamI ()));
				break;

			case Opcode.Ldloc0:
			case Opcode.Ldloc1:
			case Opcode.Ldloc2:
			case Opcode.Ldloc3:
				s.LoadVar (LocalToDic ((int)it.Opcode - (int)Opcode.Ldloc0));
				break;
			case Opcode.LdlocS:
				s.LoadVar (LocalToDic (it.DecodeParamI ()));
				break;

			case Opcode.Ldarg0:
			case Opcode.Ldarg1:
			case Opcode.Ldarg2:
			case Opcode.Ldarg3:
				s.LoadVar (ArgToDic ((int)it.Opcode - (int)Opcode.Ldarg0));
				break;

			case Opcode.StargS:
				s.StoreVar (ArgToDic (it.DecodeParamI ()));
				break;

			case Opcode.Blt:
			case Opcode.Ble:
				s.EmitCondBranch (it.Opcode, bb.To [0], bb.To [1]);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;

			case Opcode.Brfalse:
			case Opcode.Brtrue:
			case Opcode.BrfalseS:
			case Opcode.BrtrueS:
				s.EmitBoolBranch (it.Opcode, bb.To [0], bb.To [1]);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;

			case Opcode.Br:
			case Opcode.BrS:
				s.EmitBranch (bb.To [0]);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;

			case Opcode.Ret:
				if (method.Signature.ReturnType != ClrType.Void)
					s.StoreVar (0);
				s.EmitBranch (bb.To [0]);
				if (it.HasNext)
					throw new Exception ("Ret MUST be last op in a BB");
				done = true;
				break;

			case Opcode.Call:
				s.EmitCall (method.Image.LoadMethodDefOrRef (it.DecodeParamI ()));
				break;

			default:
				throw new Exception ($"Cannot emit {it.Mnemonic}");
			}
		}
		if (!done) {
			if (bb.To.Count > 1)
				throw new Exception ("Can't fall through to multiple blocks");
			if (bb.To.Count == 1)
				s.EmitBranch (bb.To [0]);
		}
		Console.WriteLine ($"AFTER TRANSLATE: {bb}");
	}
}
}
