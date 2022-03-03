namespace SocketAPI 
{
	public static class EnvParser
	{
		/// <summary>
		/// Note: input gets converted to lowercase first.
		/// </summary>
		public static System.Collections.Generic.Dictionary<string, object>? ParseFile(string path)
		{
			if (!System.IO.File.Exists(path))
				return null;

			return EnvParser.Parse(System.IO.File.ReadAllText(".env"));
		}

		/// <summary>
		/// Note: input gets converted to lowercase first.
		/// </summary>
		public static System.Collections.Generic.Dictionary<string, object>? Parse(string input)
		{
			string[] lines = input.ToLower().Split('\n');

			System.Collections.Generic.Dictionary<string, object> dotEnvEntries = new();			
			foreach(string line in lines)
			{
				string[] parameters = line.Split("=");
				
				if (parameters.Length != 2)
					continue;

				dotEnvEntries.Add(parameters[0], parameters[1]);
			}
			
			return dotEnvEntries;
		}
	}
}