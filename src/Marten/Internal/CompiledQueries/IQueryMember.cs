using System;
using System.Collections.Generic;
using System.Reflection;
using LamarCodeGeneration;
using Npgsql;

namespace Marten.Internal.CompiledQueries
{
    public interface IQueryMember
    {
        Type Type { get; }
        bool CanWrite();

        MemberInfo Member { get; }
        IList<int> ParameterIndexes { get; }
        void GenerateCode(GeneratedMethod method, StoreOptions storeOptions);
        void StoreValue(object query);
        void TryMatch(List<NpgsqlParameter> parameters, StoreOptions storeOptions);
        void TryWriteValue(UniqueValueSource valueSource, object query);
        object GetValueAsObject(object query);
    }
}
