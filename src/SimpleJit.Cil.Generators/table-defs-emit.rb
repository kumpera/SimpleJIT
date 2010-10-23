class String
  def cap
    self[0,1].upcase + self[1,999]
  end
end

class Table
  def initialize name
    @table_name = name
  end

  attr_accessor :table_name
  attr_accessor :id

  @@tables = []

  def self.tables
    @@tables
  end

  def self.id_max
    id_max = 0
    @@tables.each { |t| id_max = t.id if t.id > id_max }
    id_max
  end

  def add_field name, type
    @fields ||= []
    @fields.push [name, type]
  end

  def method_missing(method, *args, &block)
    add_field method.to_s.sub("=",""), args[0] if method.to_s =~ /.*=/
  end

  def get_type t
    return "uint" if t == :blob || t == :string || t == :guid
    return t.type_name if t.class == CodedToken
    t.to_s
  end

  def get_size t
    return 2 if t == :ushort
    return 4 if t == :uint
    return 0 if t == :blob || t == :string || t == :guid
    return 0 if t.class == CodedToken

    raise "don't know: " + t.to_s 
  end

  def dump
    puts "public struct #{@table_name}Row {"
    puts "\tconst int ID=#{id};"
    blob = 0
    string = 0
    guid = 0
    base_size = 0
    @fields.each { |f|
      ftype = get_type f[1]
      base_size += get_size f[1]
      blob += 1 if f[1] == :blob
      string += 1 if f[1] == :string
      guid += 1 if f[1] == :guid
      puts "\tinternal #{ftype} #{f[0]};"
    }
    puts ""
    puts "\tpublic static int RowSize (byte head_sizes) {"
    puts "\t\tint size = #{base_size};"
    puts "\t\tsize += Image.HeapSize (head_sizes, Image.BLOB_BIT, #{blob});" if blob > 0
    puts "\t\tsize += Image.HeapSize (head_sizes, Image.STRING_BIT, #{string});" if string > 0
    puts "\t\tsize += Image.HeapSize (head_sizes, Image.GUID_BIT, #{guid});" if guid > 0
    puts "\t\treturn size;"
    puts "\t}"
    puts "}\n\n"
  end
end

class CodedToken
  @@tokens = {}
  def self.get token
    @@tokens [token]
  end

  def self.register token
    @@tokens [token.token_name] = token
  end

  def self.dump_all
    @@tokens.each_value {|tk| tk.dump }
  end

  def initialize name
    @token_name = name
    @tables = []
  end

  attr_accessor :token_name

  def type_name
    @token_name.to_s
  end

  def << (table)
    @tables << table
  end

  def bits
    c = 1
    b = 0;
    while c < @tables.length :
      b += 1
      c = c * 2
    end
    b
  end

  def dump
    puts """
public struct #{token_name} {
  uint token;
  public const int TAG_BITS = #{bits};
  public const int MAX_TABLE_SIZE = 1 << (16 - TAG_BITS);
  static readonly Table[] ENCODED_TABLES = new Table[] {"""
    @tables.each { |t| puts "    Table.#{t}," }

    puts """  };
  public int DeriveSize (Image img) {
    return img.CodedIndexSize (MAX_TABLE_SIZE, ENCODED_TABLES);
  }
}
"""
  end
end

def table name
  tb = Table.new name
  yield tb
  Table.tables << tb
end


def coded_token name
  tk = CodedToken.new name
  yield tk
  CodedToken.register tk
end

def coded token
  CodedToken.get token
end

def index token
  :uint #FIXME
end

coded_token (:TypeDefOrRef) { |tk|
  tk << :TypeDef
  tk << :TypeRef
  tk << :TypeSpec
}

coded_token (:HasConstant) { |tk|
  tk << :Field
  tk << :Param
  tk << :Property
}

coded_token (:HasCustomAttribute) { |tk|
  tk << :MethodDef
  tk << :Field
  tk << :TypeRef
  tk << :TypeDef
  tk << :Param
  tk << :InterfaceImpl
  tk << :MemberRef
  tk << :Module
  tk << :Permission
  tk << :Property
  tk << :Event
  tk << :StandAloneSig
  tk << :ModuleRef
  tk << :TypeSpec
  tk << :Assembly
  tk << :AssemblyRef
  tk << :File
  tk << :ExportedType
  tk << :ManifestResource
  tk << :GenericParam
}

coded_token (:HasFieldMarshall) { |tk|
  tk << :Field
  tk << :Param
}

coded_token (:HasDeclSecurity) { |tk|
  tk << :TypeDef
  tk << :MethodDef
  tk << :Assembly
}

coded_token (:MemberRefParent) { |tk|
  tk << :TypeDef
  tk << :TypeRef
  tk << :ModuleRef
  tk << :MethodDef
  tk << :TypeSpec
}

coded_token (:HasSemantics) { |tk|
  tk << :Event
  tk << :Property
}

coded_token (:MethodDefOrRef) { |tk|
  tk << :MethodDef
  tk << :MemberRef
}

coded_token (:MethodDefOrRef) { |tk|
  tk << :MethodDef
  tk << :MemberRef
}

coded_token (:MemberForwarded) { |tk|
  tk << :Field
  tk << :MethodDef
}

coded_token (:Implementation) { |tk|
  tk << :File
  tk << :AssemblyRef
  tk << :ExportedType
}

coded_token (:CustomAttributeType) { |tk|
  tk << :NotUsed
  tk << :NotUsed
  tk << :MethodDef
  tk << :MemberRef
  tk << :NotUsed
}

coded_token (:ResolutionScope) { |tk|
  tk << :Module
  tk << :ModuleRef
  tk << :AssemblyRef
  tk << :TypeRef
}

coded_token (:TypeOrMethodDef) { |tk|
  tk << :TypeDef
  tk << :MethodDef
}

table (:Module) { |tb|
  tb.id = 0x00
  tb.generation = :ushort
  tb.name = :string
  tb.mvid = :guid
  tb.encId = :guid
  tb.encBaseid = :guid
}

table (:TypeRef) { |tb|
  tb.id = 0x01
  tb.resolutionScope = coded :ResolutionScope
  tb.typeName = :string
  tb.typeNamespace = :string
}

table (:TypeDef) { |tb|
  tb.id = 0x02
  tb.flags = :uint #enum:TypeAttribute
  tb.typeName = :string
  tb.typeNamespace = :string
  tb.extends = coded :TypeDefOrRef
  tb.fieldList = index :Field
  tb.methodList = index :MethodDef
}

table (:Field) { |tb|
  tb.id = 0x04
  tb.flags = :ushort #enum:FieldAttributes
  tb.name = :string
  tb.signatre = :blob
  tb.paramList = index :Param
}

table (:MethodDef) { |tb|
  tb.id = 0x06
  tb.rva = :uint
  tb.implFlags = :ushort #enum:MethodImplFlags
  tb.flags = :ushort #enum:MethodFlags
  tb.name = :string
  tb.signatre = :blob
  tb.paramList = index :Param
}

table (:Param) { |tb|
  tb.id = 0x08
  tb.flags = :ushort #enum:ParamAttributes
  tb.sequence = :ushort
  tb.name = :string
}

table (:MemberRef) { |tb|
  tb.id = 0x0A
  tb.parent = coded :MemberRefParent
  tb.name = :string
  tb.signature = :blob
}

table (:CustomAttribute) { |tb|
  tb.id = 0x0C
  tb.parent = coded :HasCustomAttribute
  tb.cattrType = coded :CustomAttributeType
  tb.value = :blob
}

table (:ModuleRef) { |tb|
  tb.id = 0x1A
  tb.name = :string
}

table (:Assembly) { |tb|
  tb.id = 0x20
  tb.hashAlg = :uint
  tb.majorVersion = :ushort
  tb.minorVersion = :ushort
  tb.buildNumber = :ushort
  tb.revisionNumber = :ushort
  tb.flags = :uint #"enum:AssemblyFlags"
  tb.publicKey = :blob
  tb.name = :string
  tb.culture = :string
}

table (:AssemblyRef) { |tb|
  tb.id = 0x23
  tb.majorVersion = :ushort
  tb.minorVersion = :ushort
  tb.buildNumber = :ushort
  tb.revisionNumber = :ushort
  tb.flags = :uint #"enum:AssemblyFlags"
  tb.publicKey = :blob
  tb.name = :string
  tb.culture = :string
  tb.hashValue = :blob
}

puts "using System;\n\nnamespace BasicJit.CIL {\n\n"
CodedToken.dump_all
Table.tables.each {|tb| tb.dump }

puts """
public enum Table {
"""
Table.tables.each {|tb|
  puts "  #{tb.table_name} = #{tb.id},"
}
puts """  MaxTableId = #{Table.id_max},
  NotUsed,
  Invalid
}
"""

puts """
public static class TableDecoder {

  public static int DecodeRowSize (int table, byte heap_sizes) {
    switch (table) {
"""
Table.tables.each {|tb|
  puts "    case #{tb.id}: return #{tb.table_name}Row.RowSize (heap_sizes);"
}

puts """    default: throw new Exception (\"Can't decode table \" + table);
    }
  }
}

}
"""
