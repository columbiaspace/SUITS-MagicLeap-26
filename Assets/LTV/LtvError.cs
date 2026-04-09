using System;
using System.Collections.Generic;

namespace LtvDiagnostics
{
    public class LtvError : IComparable<LtvError>
    {
        public string Code { get; }
        public string Description { get; }
        public bool NeedsResolved { get; }
        public List<string> Procedures { get; }
        public int Priority { get; }
        public int Criticality { get; }
        public int SubsystemId { get; }

        public LtvError(string code, string description, bool needsResolved, List<string> procedures)
        {
            Code = code ?? "0000";
            Description = description ?? string.Empty;
            NeedsResolved = needsResolved;
            Procedures = procedures ?? new List<string>();

            Criticality = ParseDigit(Code, 0);
            SubsystemId = ParseDigit(Code, 1);
            Priority = Criticality * 10 + SubsystemId;
        }

        public int CompareTo(LtvError other)
        {
            if (other == null)
            {
                return 1;
            }

            return Priority.CompareTo(other.Priority);
        }

        private static int ParseDigit(string code, int index)
        {
            if (string.IsNullOrEmpty(code) || index >= code.Length)
            {
                return 0;
            }

            char c = code[index];
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            return 0;
        }
    }
}
