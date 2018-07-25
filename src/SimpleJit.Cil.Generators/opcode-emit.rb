require 'rexml/document'

include REXML

def is_numeric str
	return str.to_i.to_s == str
end

def opcodeName name
	name = name.gsub(".", " ").gsub("_", " ").split.collect {|i| i.capitalize}
	res =""
	last=nil
	name.each {|part|
		if last == nil
			res += part
		else
			if is_numeric(part) && is_numeric(last[last.size - 1, 1])
				res += "_" + part
			else
				res += part;
			end	
		end
		last = part
	}
	return res
end

def is_normal_opcode el
	return el.attributes['op1'] == "0xff"
end

def is_extended_opcode el
	return el.attributes['op1'] == "0xfe"
end

def max a, b
	return a > b ? a : b
end

def enumLine el
	name = opcodeName el.attributes['name']

	value = el.attributes['op2']
	puts "\t\t#{name} = #{value},"
end

def writeEnum doc
	puts "\tpublic enum Opcode : byte"
	puts "\t{"

	doc.root.elements.each { |el|
		enumLine(el) if is_normal_opcode(el)
	}
	puts "\t\tExtendedPrefix   = 0xFE"
	puts "\t}"
end


def writeExtendedEnum doc
	puts "\tpublic enum ExtendedOpcode : byte"
	puts "\t{"

	doc.root.elements.each { |el|
		enumLine(el) if is_extended_opcode(el)
	}
	puts "\t}"
end

def writeOpcodeInit el
	table = "traits"
    table = "extendedTraits" if is_extended_opcode(el)

	isExt = "false"
    isExt = "true" if is_extended_opcode(el)

	flags = ""
	case el.attributes['flowcontrol']
	when "Next"
	  flags = "OpcodeFlags.Next"
	when "Branch"
	  flags = "OpcodeFlags.Branch"
	when "Cond_Branch"
	  flags = "OpcodeFlags.CondBranch"
	when "Break"
	  flags = "OpcodeFlags.Break"
	when "Return"
	  flags = "OpcodeFlags.Return"
	when "Call"
	  flags = "OpcodeFlags.Call"
	when "Throw"
	  flags = "OpcodeFlags.Throw"
	when "Meta" #Meta is used by prefixes
	  flags = "OpcodeFlags.Next"
	else
	  raise "unknown flag #{el.attributes['flowcontrol']} for opcode #{el.attributes['name']}"
	end

	case el.attributes['opcodetype']
	when "Macro"
	  flags = "#{flags} | OpcodeFlags.Macro"
	when "Objmodel"
	  flags = "#{flags} | OpcodeFlags.ObjectModel"
	when "Prefix"
	  flags = "#{flags} | OpcodeFlags.Prefix"
	when "Primitive"
	  flags = "#{flags} | OpcodeFlags.Primitive"
	else
	  raise "unknown opcode type #{el.attributes['opcodetype']} for opcode #{el.attributes['name']}"
	end

	case (el.attributes['operandtype'])
	  when "InlineNone"
	    flags = "#{flags} | OpcodeFlags.NoOperand"
	  when "ShortInlineBrTarget", "ShortInlineI", "ShortInlineParam", "ShortInlineVar"
	    flags = "#{flags} | OpcodeFlags.OperandSize1"
	  when "InlineParam"
	    flags = "#{flags} | OpcodeFlags.OperandSize2"
	  when "ShortInlineR", "InlineBrTarget", "InlineField", "InlineI", "InlineMethod", "InlineSig", "InlineString", "InlineTok", "InlineType", "InlineVar"
	    flags = "#{flags} | OpcodeFlags.OperandSize4"
	  when "InlineI8", "InlineR"
	    flags = "#{flags} | OpcodeFlags.OperandSize8"
	  when "InlineSwitch"
	    flags = "#{flags} | OpcodeFlags.OperandSize4"
	  else
	    raise "unknown opcode operand type #{el.attributes['operandtype']} for opcode #{el.attributes['name']}"
	end

	puts "\t\t\t#{table} [#{el.attributes['op2']}] = new OpcodeTraits (#{flags}, \"#{opcodeName(el.attributes['name'])}\", #{el.attributes['op2']}, #{isExt}, PopBehavior.#{el.attributes['stackbehaviourpop']}, PushBehavior.#{el.attributes['stackbehaviourpush']} );" 
end

def writeInvalidOpcode op, table, isExt
	puts "\t\t\t#{table} [0x#{op.to_s 16}] = new OpcodeTraits (OpcodeFlags.Invalid, null, 0x#{op.to_s 16}, #{isExt}, PopBehavior.Pop0, PushBehavior.Push0);" 
end

def writeOpcodeTable doc
	op_hash = Hash.new
	ext_hash = Hash.new
	op_max = 0;
	ext_max = 0;
	doc.root.elements.each { |el|
		op_2 = el.attributes['op2'].to_i 16
		op_max = max(op_max, op_2) if is_normal_opcode el
		ext_max = max(ext_max, op_2) if is_extended_opcode el

		op_hash[op_2] = el if is_normal_opcode el
		ext_hash[op_2] = el if is_extended_opcode el
	}

  puts """
\tpublic static class TraitsLookup {
\t\tprivate static OpcodeTraits[] traits;
\t\tprivate static OpcodeTraits[] extendedTraits;

		public static void Decode (byte opcode, out OpcodeTraits res) {
			if (opcode > #{op_max})
				res = new OpcodeTraits (OpcodeFlags.Invalid, null, opcode, false, PopBehavior.Pop0, PushBehavior.Push0);
			else
				res = traits [opcode];
		}

		public static void DecodeExtended (byte opcode, out OpcodeTraits res) {
			if (opcode > #{ext_max})
				res = new OpcodeTraits (OpcodeFlags.Invalid, null, opcode, true, PopBehavior.Pop0, PushBehavior.Push0);
			else
				res = extendedTraits [opcode];
		}

\t\tstatic TraitsLookup() {
\t\t\ttraits = new OpcodeTraits [#{op_max + 1}];
\t\t\textendedTraits = new OpcodeTraits [#{ext_max + 1}];

"""

	0.upto(op_max) { |i|
		if op_hash[i]
			writeOpcodeInit op_hash[i]
		else
			writeInvalidOpcode i, "traits", "false"
		end
	}
	0.upto(ext_max) { |i|
		if ext_hash[i]
			writeOpcodeInit ext_hash[i]
		else
			writeInvalidOpcode i, "extendedTraits", "true"
		end
	}

	#doc.root.elements.each { |el| writeOpcodeInit el }   	 
puts """
\t\t}
\t}
"""
end

file = File.new($0.gsub("opcode-emit.rb", "opcodes.xml"))

doc = Document.new(file)
puts """
using System;

namespace SimpleJit.CIL
{

"""
writeEnum doc 
puts ""
writeExtendedEnum doc
puts ""
writeOpcodeTable doc


puts "}"