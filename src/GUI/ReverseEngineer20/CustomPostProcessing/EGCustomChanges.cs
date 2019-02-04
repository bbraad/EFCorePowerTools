using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Data.SqlClient;
using System.Reflection;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Globalization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Data;
using System.ComponentModel.DataAnnotations;

namespace ReverseEngineer20.CustomPostProcessing
{
	class EGCustomChanges
	{
		internal static void Generate(IList<string> filesWithoutContectAndMergeFile, string connectionString, string filePath, bool excludeStringLengthAttribute)
		{
			UpdateFiles(filesWithoutContectAndMergeFile, excludeStringLengthAttribute);
			CompilerResults cpResults = BuildAssembly(filesWithoutContectAndMergeFile);
			CreateDbInfo(cpResults.CompiledAssembly.GetTypes(), connectionString, filePath);
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


		//[Obsolete("Scaffold tilføjer allerede filerne til projektet, så det giver ikke mening at merge dem også, da de enkelte filer så skal fjernes fra projektet igen.")]
		//private static StringBuilder CreateMergedClasses(IList<string> files, bool excludeStringLengthAttribute)
		//{
		//	bool isFirst = true;
		//	Dictionary<string, Type> types = new Dictionary<string, Type>();
		//	StringBuilder mergedFile = new StringBuilder();
		//	foreach (string file in files)
		//	{
		//		var fileName = Path.GetFileNameWithoutExtension(file);

		//		string[] typeLines = File.ReadAllLines(file);

		//		bool startWriting = false;

		//		foreach (string line in typeLines)
		//		{
		//			if (excludeStringLengthAttribute && line.Contains("[StringLength(")) //Vi ignorerer linjer med [StringLength(xxx)]
		//			{
		//				continue;
		//			}

		//			var resultLine = line.Replace("[Required]", "[Required(AllowEmptyStrings=true)]");
		//			resultLine = resultLine.Replace("&#230;", "æ");

		//			if (isFirst)
		//			{

		//				//Build using lines and namespace lines.
		//				if (resultLine.StartsWith("}"))
		//				{
		//					//Ignore this line
		//					isFirst = false;

		//				}
		//				else
		//				{
		//					mergedFile.AppendLine(resultLine);
		//				}
		//			}
		//			else
		//			{

		//				if (resultLine.StartsWith("{"))
		//				{
		//					startWriting = true;
		//				}
		//				else if (resultLine.StartsWith("}"))
		//				{
		//					//Just ignore the line
		//				}
		//				else
		//				{
		//					if (startWriting)
		//					{
		//						mergedFile.AppendLine(resultLine);
		//					}
		//				}
		//			}
		//		}
		//		mergedFile.AppendLine(""); //Empty line between classes.
		//	}

		//	mergedFile.AppendLine("}"); //End namespace

		//	return mergedFile;
		//}





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

