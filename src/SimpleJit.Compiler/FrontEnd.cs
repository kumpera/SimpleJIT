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
	Stack <Ins> stack = new Stack <Ins> ();
	BasicBlock bb;

	Stack <StackValue> stack2 = new Stack <StackValue> ();
	Dictionary <int, int> varToVreg = new Dictionary <int, int> ();
	
	public EvalStack (BasicBlock bb) {
		this.bb = bb;
		Console.WriteLine ("initial var map:");
		foreach (var v in bb.InVars) {
			varToVreg [v] = bb.NextReg ();
			Console.WriteLine ($"\t{v} == {varToVreg [v]}");
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
		default: throw new Exception ($"{op} is not a condop");
		}
	}

	static Ops InvertCond (Ops op) {
		switch (op) {
		case Ops.Ble: return Ops.Bg;
		case Ops.Blt: return Ops.Bge;
		case Ops.Bg: return Ops.Ble;
		case Ops.Bge: return Ops.Blt;
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
				Dest = bb.NextReg (),
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
				branchOp = CilToCondOp (cond);
			} else {
				c = r1.value;
				vreg = r0.value;
				branchOp = InvertCond (CilToCondOp (cond));
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

	public void EmitBranch (BasicBlock to) {
		Console.WriteLine ("Branch BB{0}", to.Number);
		CallInfo info = new CallInfo (varToVreg, to);

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
		// Console.WriteLine ("initial var map:");
		// foreach (var v in bb.InVars) {
		// 	varTable [v] = bb.NextReg ();
		// 	Console.WriteLine ($"\t{v} == {varTable [v]}");
		// }

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
				s.StoreVar (1 + ((int)it.Opcode - (int)Opcode.Stloc0));
				// varTable [1 + ((int)it.Opcode - (int)Opcode.Stloc0)] = s.StoreVar ();
				break;
			case Opcode.StlocS:
				s.StoreVar (1 + it.DecodeParamI ());
				// varTable [1 + it.DecodeParamI ()] = s.StoreVar ();
				break;

			case Opcode.Ldloc0:
			case Opcode.Ldloc1:
			case Opcode.Ldloc2:
			case Opcode.Ldloc3:
				// s.LoadVar (varTable [1 + ((int)it.Opcode - (int)Opcode.Ldloc0)]);
				s.LoadVar (1 + ((int)it.Opcode - (int)Opcode.Ldloc0));
				break;
			case Opcode.LdlocS:
				// s.LoadVar (varTable [1 + it.DecodeParamI ()]);
				s.LoadVar (1 + it.DecodeParamI ());
				break;

			case Opcode.Ldarg0:
			case Opcode.Ldarg1:
			case Opcode.Ldarg2:
			case Opcode.Ldarg3:
				//s.LoadVar (varTable [-1 - ((int)it.Opcode - (int)Opcode.Ldarg0)]);
				s.LoadVar (-1 - ((int)it.Opcode - (int)Opcode.Ldarg0));
				break;

			case Opcode.StargS:
				// varTable [-1 - it.DecodeParamI ()] = s.StoreVar ();
				s.StoreVar (-1 - it.DecodeParamI ());
				break;

			case Opcode.Blt:
			case Opcode.Ble:
				// Console.WriteLine ("varTable before cond:");
				// foreach (var kv in varTable)
				// 	Console.WriteLine ($"\t{kv.Key} -> {kv.Value}");

				// var infos = new CallInfo [2];
				// infos [0] = new CallInfo (varTable, bb.To [0]);
				// infos [1] = new CallInfo (varTable, bb.To [1]);

				// s.EmitCondBranch (it.Opcode, infos);
				s.EmitCondBranch (it.Opcode, bb.To [0], bb.To [1]);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;

			case Opcode.Br:
				Console.WriteLine ($"BB TO LEN {bb.To.Count}");
				// s.EmitBranch (new CallInfo (varTable, bb.To [0]));
				s.EmitBranch (bb.To [0]);
				if (it.HasNext)
					throw new Exception ("Branch MUST be last op in a BB");
				done = true;
				break;

			case Opcode.Ret:
				if (method.Signature.ReturnType != ClrType.Void)
					s.StoreVar (0);
					// varTable [0] = s.StoreVar ();
				//s.EmitBranch (new CallInfo (varTable, bb.To [0]));
				s.EmitBranch (bb.To [0]);
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
				// s.EmitBranch (new CallInfo (varTable, bb.To [0]));
				s.EmitBranch (bb.To [0]);
		}
	}
}
}
