using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Data.SqlClient;
using System.Reflection;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Data;
using System.ComponentModel.DataAnnotations;
using System.CodeDom.Compiler;
using Microsoft.CSharp;

namespace ReverseEngineer20
{


	class EGCustomChanges
	{
		internal static void Generate(IList<string> filesWithoutContextAndMergeFile, string connectionString, string filePath, string contextName, string namespacePath)
		{
			bool excludeStringLengthAttribute = true;
			UpdateFiles(filesWithoutContextAndMergeFile, excludeStringLengthAttribute);
			CompilerResults cpResults = BuildAssembly(filesWithoutContextAndMergeFile);
			CreateDbInfo(cpResults.CompiledAssembly.GetTypes(), connectionString, filePath);
			CreateMyDbContext(cpResults.CompiledAssembly.GetTypes(), connectionString, filePath, contextName, namespacePath);
		}

		private static void UpdateFiles(IList<string> files, bool excludeStringLengthAttribute)
		{
			foreach (string file in files)
			{
				StringBuilder mergedFile = new StringBuilder();
				string[] typeLines = File.ReadAllLines(file);

				foreach (string line in typeLines)
				{
					if (excludeStringLengthAttribute && line.Contains("[StringLength(")) //Vi ignorerer linjer med [StringLength(xxx)]
					{
						continue;
					}

					var resultLine = line.Replace("[Required]", "[Required(AllowEmptyStrings=true)]");
					resultLine = resultLine.Replace("&#230;", "æ");
					mergedFile.AppendLine(resultLine);
				}

				File.WriteAllText(file, mergedFile.ToString(), Encoding.UTF8);
			}
		}


		private static void CreateDbInfo(IEnumerable<Type> types, string connectionString, string filePath)
		{
			//Connect to db and read all table information.

			DbConnection conn = new SqlConnection(connectionString);
			conn.Open();

			IDbCommand command = conn.CreateCommand();
			command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, TABLE_SCHEMA FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME";
			IDataReader reader = command.ExecuteReader();

			Dictionary<string, DatabaseTable> tables = new Dictionary<string, DatabaseTable>();

			string prevRowtableName = null;
			while (reader.Read())
			{
				string tableName = (string)reader["TABLE_NAME"];
				string tableSchema = (string)reader["TABLE_SCHEMA"];

				DatabaseTable table = null;
				if (string.Equals(prevRowtableName, tableName))
				{
					table = tables[tableName.ToLower()];
				}
				else
				{
					table = new DatabaseTable();
					table.Name = tableName;
					table.Schema = tableSchema;
					tables.Add(tableName.ToLower(), table);
					prevRowtableName = tableName;
				}

				string colName = (string)reader["COLUMN_NAME"];
				string colDatatype = (string)reader["DATA_TYPE"];

				table.Columns.Add(colName, colDatatype);
			}

			List<DbInfoLine> dbinfoLines = new List<DbInfoLine>();

			foreach (DatabaseTable table in tables.Values)

			//Run through types and find columns in database and in poco.
			//foreach (Type type in types.OrderBy(t => t.Name))
			{
				//DatabaseTable table = tables[type.Name.ToLower()];
				Type type = types.SingleOrDefault(t => t.Name.ToLower() == table.Name.ToLower());

				DbInfoLine dboLine = new DbInfoLine();
				dboLine.Value1 = table.Schema;
				dboLine.Value2 = table.Name;

				dboLine.Value3 = type?.Name ?? table.Name;
				dbinfoLines.Add(dboLine);

				foreach (KeyValuePair<string, string> colNameDatatype in table.Columns)
				{
					DbInfoLine line = new DbInfoLine();
					line.Value1 = colNameDatatype.Key;
					line.Value2 = (type != null) ? FindPropertyNameInPoco(type, colNameDatatype.Key) : colNameDatatype.Key;
					line.Value3 = colNameDatatype.Value;
					dbinfoLines.Add(line);
				}

				DbInfoLine emptyLine = new DbInfoLine();
				dbinfoLines.Add(emptyLine);
			}

			WriteDbInfo(dbinfoLines, filePath);
		}

		/// <summary>
		/// Write dbinfo lines to dbinfo.txt
		/// </summary>
		/// <param name="dbinfoLines"></param>
		/// <param name="filePath"></param>
		private static void WriteDbInfo(List<DbInfoLine> dbinfoLines, string filePath)
		{
			StringBuilder sb = new StringBuilder();

			foreach (DbInfoLine line in dbinfoLines)
			{
				sb.AppendLine($"{line.Value1}\t{line.Value2}\t{line.Value3}");
			}

			//One level up
			var superFolder = Directory.GetParent(filePath).FullName;
			File.WriteAllText(Path.Combine(superFolder, "DbInfo.txt"), sb.ToString());
		}

		private static string FindPropertyNameInPoco(Type pocoType, string propertyName)
		{
			//Find from attribute [Column]
			foreach (PropertyInfo x in pocoType.GetProperties())
			{
				ColumnAttribute attr = (ColumnAttribute)x.GetCustomAttribute(typeof(ColumnAttribute));

				if (attr != null && attr.Name != null && attr.Name.ToLower() == propertyName.ToLower())
				{
					return x.Name;
				}
			}

			//Find from property name.
			foreach (PropertyInfo x in pocoType.GetProperties())
			{
				if (x.Name.ToLower() == propertyName.ToLower())
				{
					return x.Name;
				}
			}

			return null;
		}


		private static CompilerResults BuildAssembly(IList<string> typePath)
		{
			CompilerParameters compilerparams = new CompilerParameters();
			compilerparams.GenerateExecutable = false;
			compilerparams.GenerateInMemory = true;
			compilerparams.ReferencedAssemblies.Add(typeof(StringLengthAttribute).Assembly.Location);
			compilerparams.ReferencedAssemblies.Add(typeof(HashSet<string>).Assembly.Location);
			compilerparams.ReferencedAssemblies.Add("System.dll");

			CSharpCodeProvider provider = new CSharpCodeProvider();
			CompilerResults results = provider.CompileAssemblyFromFile(compilerparams, typePath.ToArray());

			if (results.Errors.HasErrors)
			{
				StringBuilder errors = new StringBuilder("Compiler Errors :\r\n");
				foreach (CompilerError error in results.Errors)
				{
					errors.AppendFormat("Line {0},{1}\t: {2}\n",
							 error.Line, error.Column, error.ErrorText);
				}
				throw new Exception(errors.ToString());
			}
			return results;
		}
		private static Type GetSpecifiedType(CompilerResults results, string typeName)
		{
			var returnType = results.CompiledAssembly.DefinedTypes.Single(t => t.Name == typeName);
			return returnType.AsType();
		}

		private static void CreateMyDbContext(Type[] types, string connectionString, string filePath, string contextName, string namespaceName)
		{
			string interfacename = $"I{contextName}";
			string FILENAMEWITHEXSTENSION = $"{interfacename}Genereret.cs";

			StringBuilder imydbcontextStrings = new StringBuilder();

			imydbcontextStrings.AppendLine("#pragma warning disable 1591    //  Ignore \"Missing XML Comment\" warning");
			imydbcontextStrings.AppendLine("using System;");
			imydbcontextStrings.AppendLine("using System.Collections.Generic;");
			imydbcontextStrings.AppendLine("using System.Data.Common;");
			imydbcontextStrings.AppendLine("using Microsoft.EntityFrameworkCore;");
			imydbcontextStrings.AppendLine(string.Empty);
			imydbcontextStrings.AppendLine($"namespace {namespaceName}");
			imydbcontextStrings.AppendLine("{");
			imydbcontextStrings.AppendLine("\t/// <summary>");
			imydbcontextStrings.AppendLine($"\t/// Auto-genereret {interfacename} fil.");
			imydbcontextStrings.AppendLine("\t/// </summary>");
			imydbcontextStrings.AppendLine($"\tpublic partial interface {interfacename} : IDisposable");
			imydbcontextStrings.AppendLine("\t{");

			foreach (var type in types)
			{
				imydbcontextStrings.AppendLine("\t\tDbSet<" + type.Name + "> " + type.Name + " { get; set; }");
			}
			imydbcontextStrings.AppendLine("\t}");
			imydbcontextStrings.AppendLine("}");

			File.WriteAllText(Path.Combine(filePath, FILENAMEWITHEXSTENSION), imydbcontextStrings.ToString());
		}



		class DatabaseTable
		{
			public DatabaseTable()
			{
				Columns = new Dictionary<string, string>();
			}
			public string Name { get; set; }
			public string Schema { get; set; }
			public Dictionary<string, string> Columns { get; set; }
		}

		class DbInfoLine
		{
			public string Value1 { get; set; }
			public string Value2 { get; set; }
			public string Value3 { get; set; }
		}
	}
}

