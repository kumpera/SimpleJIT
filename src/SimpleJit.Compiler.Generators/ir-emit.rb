puts "//Generated IR ops"

class InsDef
  def initialize name
    self.name = name
    self.dreg = self.is_call = false
    self.args = self.consts = self.call_infos = 0
    self.cat = {}
  end

  attr_accessor :name, :dreg, :args, :consts, :call_infos, :fmt, :is_call, :cat

  @@insts = []

  def self.insts
    @@insts
  end

  def sigAppend (sig, arg)
    if (sig == "")
      return arg
    end
    return sig + ", " + arg
  end

  def genSig
    sig = ""
    sig = sigAppend(sig, "int dreg") if dreg
    (0...args).each { |i| sig = sigAppend(sig, "int r#{i}") }
    (0...consts).each { |i| sig = sigAppend(sig, "int const#{i}") }
    (0...call_infos).each { |i| sig = sigAppend(sig, "CallInfo ci#{i}") }
    sig = sigAppend(sig, "MethodData method, int[] args") if is_call
    sig
  end

  def genCtor
    sig = genSig
    puts "\tpublic static Ins New#{name} (#{sig}) {"
    puts "\t\treturn new Ins (Ops.#{name}) {"
    puts "\t\t\tDest = dreg," if dreg
    (0...args).each { |i| puts "\t\t\tR#{i} = r#{i}," }
    (0...consts).each { |i| puts "\t\t\tConst#{i} = const#{i}," }
    if call_infos > 0 then
      print "\t\t\tCallInfos = new CallInfo[] { "
      (0...call_infos).each { |i| print "ci#{i}, " }
      puts "},"
    end
    if is_call then
      puts "\t\t\tMethod = method, "
      puts "\t\t\tCallVars = args, "
    end
    puts "\t\t};"
    puts "\t}"
    puts ""
  end

  def genToString
    d = " {DStr} <=" if dreg
    a = ""
    (0...args).each { |i| a += " {R#{i}Str}" }
    c = ""
    (0...consts).each { |i| c += " [{Const#{i}}]" }
    ci = ""
    (0...call_infos).each { |i| ci += " {CallInfos [#{i}]}" }
    str = fmt || "{Op}#{d}#{a}#{c}#{ci}"
    puts "\t\tcase Ops.#{name}: return $\"#{str}\";"
  end
end

def inst name
  ins = InsDef.new name
  yield ins if block_given?
  InsDef.insts << ins
end

def unop name
  ins = InsDef.new name
  ins.dreg = true
  ins.args = 1
  ins.cat[:RA] = :DA1
  yield ins if block_given?
  InsDef.insts << ins
end

def binop name
  ins = InsDef.new name
  ins.dreg = true
  ins.args = 2
  ins.cat[:RA] = :DA2

  yield ins if block_given?
  InsDef.insts << ins
end

def binop_const name
  ins = InsDef.new name
  ins.dreg = true
  ins.args = 1
  ins.consts = 1
  ins.cat[:RA] = :DA1

  yield ins if block_given?
  InsDef.insts << ins
end

def bin_inst name
  ins = InsDef.new name
  ins.args = 2
  ins.cat[:RA] = :A2

  yield ins if block_given?
  InsDef.insts << ins
end

def bin_inst_const name
  ins = InsDef.new name
  ins.args = 1
  ins.consts = 1
  ins.cat[:RA] = :A1

  yield ins if block_given?
  InsDef.insts << ins
end

def use_const name
  ins = InsDef.new name
  ins.dreg = true
  ins.consts = 1
  ins.cat[:RA] = :D
 
  yield ins if block_given?
  InsDef.insts << ins
end

def cond_branch name
  ins = InsDef.new name
  ins.call_infos = 2
  ins.cat[:RA] = :BR2

  yield ins if block_given?
  InsDef.insts << ins
end

def branch name
  ins = InsDef.new name
  ins.call_infos = 1
  ins.cat[:RA] = :BR1

  yield ins if block_given?
  InsDef.insts << ins
end


use_const (:IConst)
unop (:Mov)
binop (:Add)
bin_inst (:Cmp)

cond_branch (:Ble)
cond_branch (:Blt)
cond_branch (:Bg)
cond_branch (:Bge)
cond_branch (:Bne)
cond_branch (:Beq)

branch (:Br)

inst (:Nop)

#RA op
inst (:SetRet) { |i|
  i.args = 1
  i.cat[:RA] = :A1R #we should compute the RA groups based on args + clobs
}

#Pseudo ops used by reg alloc
use_const (:LoadArg) { |i|
  i.cat[:RA] = :ARG
  i.fmt = "{Op} {DStr} <= REG_ARG [{Const0}]"
}

inst (:SpillVar) { |i|
  i.args = 1
  i.consts = 1

  i.fmt = "{Op} [{Const0}] <= {R0Str}"
}

inst (:SpillConst) { |i|
  i.consts = 2

  i.fmt = "{Op} [{Const0}] <= [{Const1}]"
}

use_const (:FillVar) { |i| 
  i.cat.delete (:RA) #RA pseudo-ops should not be processed
}


#Early ISEL ops
binop_const (:AddI) { |i|
  i.cat[:RA] = :DA1Clob
}
bin_inst_const (:CmpI)

# Call ops

inst (:Call) { |i|
  i.dreg = true
  i.is_call = true
  i.cat[:RA] = :ICall

  i.fmt = "{Op} {DStr} <= {Method.Name} ({CallArgsStr})"
}

inst (:VoidCall) { |i|
  i.is_call = true
  i.cat[:RA] = :VCall
  i.fmt = "{Op} {Method.Name} ({CallArgsStr})"
}


#RegAlloc helper, MUST NOT BE USED outside as violates use/def sempantics of Ins

inst (:Swap) { |i|
  i.args = 2
  i.fmt = "{Op} {R0Str} <> {R1Str}"
}


puts "using System;"
puts "using SimpleJit.Metadata;"
puts ""
puts "namespace SimpleJit.Compiler {"
puts " public enum Ops {"
InsDef.insts.each { |i|
  puts "\t#{i.name},"
}
puts "}"

puts "public partial class Ins {"
#meat of Ins

InsDef.insts.each { |i|
  i.genCtor()
}

puts "\tpublic override string ToString () {"
puts "\t\tswitch (this.Op) {"
InsDef.insts.each { |i|
  i.genToString()
}
puts "\t\tdefault: throw new Exception ($\"Unknown op {Op}\");"
puts "\t\t}"
puts "\t}"

puts "}"

#RA Category
puts "public enum RegAllocCat {"
ins_to_cat = {}
InsDef.insts.each { |i|
  cat = i.cat[:RA]
  if cat then
    puts "\t\t#{cat}," unless ins_to_cat[cat]
    ins_to_cat[cat] = [] unless ins_to_cat[cat]
    ins_to_cat[cat] << i
  end
}

puts "}"

puts "public static class RegAllocCatExtensions {"
puts "\tpublic static RegAllocCat GetRegAllocCat (this Ins ins) {"
puts "\t\tswitch (ins.Op) {"
ins_to_cat.each_pair { |k, v| 
  v.each { |i| puts "\t\tcase Ops.#{i.name}:"}
  puts "\t\t\treturn RegAllocCat.#{k};"
  puts "\t\t\tbreak;"
}

puts "\t\tdefault:"
puts "\t\t\tthrow new Exception ($\"Invalid op {ins} has no RegAllocCat\");"
puts "\t\t}";
puts "\t}"
puts "}"

puts "}"
