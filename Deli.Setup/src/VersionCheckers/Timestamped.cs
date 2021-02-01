using System;
using Newtonsoft.Json;

namespace Deli.Setup
{
	internal readonly struct Timestamped<T>
	{
		public static Timestamped<T> Now(T content) => new(DateTime.UtcNow, content);

		public DateTime TimeUtc { get; }
		public T Content { get; }

		[JsonConstructor]
		public Timestamped(DateTime timeUtc, T content)
		{
			TimeUtc = timeUtc;
			Content = content;
		}

		public static implicit operator T(Timestamped<T> self) => self.Content;
	}
}
