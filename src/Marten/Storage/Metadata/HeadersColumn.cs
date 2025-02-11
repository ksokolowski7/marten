using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;

namespace Marten.Storage.Metadata
{
    internal class HeadersColumn: MetadataColumn<Dictionary<string, object>>, IEventTableColumn
    {
        public static readonly string ColumnName = "headers";

        public HeadersColumn() : base(ColumnName, x => x.Headers)
        {
            Type = "jsonb";
            Enabled = false;
        }

        internal override async Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false))
            {
                return;
            }

            metadata.Headers = await martenSession.Serializer.FromJsonAsync<Dictionary<string, object>>(reader, index, token).ConfigureAwait(false);
        }

        internal override void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader)
        {
            if (reader.IsDBNull(index))
            {
                return;
            }

            var json = reader.GetStream(index);
            metadata.Headers = martenSession.Serializer.FromJson<Dictionary<string, object>>(reader, index);
        }

        internal override void RegisterForLinqSearching(DocumentMapping mapping)
        {
            // Nothing
        }

        internal override UpsertArgument ToArgument()
        {
            return new HeadersArgument();
        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.IfDbReaderValueIsNotNull(index, () =>
            {
                method.AssignMemberFromReader<IEvent>(null, index, x => x.Headers);
            });
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.IfDbReaderValueIsNotNullAsync(index, () =>
            {
                method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.Headers);
            });
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            method.Frames.Code($"parameters[{index}].NpgsqlDbType = {{0}};", NpgsqlDbType.Jsonb);
            method.Frames.Code($"parameters[{index}].Value = {{0}}.Serializer.ToJson({{1}}.{nameof(IEvent.Headers)});", Use.Type<IMartenSession>(), Use.Type<IEvent>());
        }
    }

    internal class HeadersArgument: UpsertArgument
    {
        public HeadersArgument()
        {
            Arg = "headerDict";
            Column = HeadersColumn.ColumnName;
            PostgresType = "jsonb";
            DbType = NpgsqlDbType.Jsonb;
        }

        public override void GenerateCodeToModifyDocument(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            if (mapping.Metadata.Headers.Member != null)
            {
                method.Frames.Code($"var headers = {{0}}.{nameof(IMartenSession.Headers)};",
                    Use.Type<IMartenSession>());
                method.Frames.SetMemberValue(mapping.Metadata.Headers.Member, "headers", mapping.DocumentType, type);
            }
        }

        public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            method.Frames.Code($"setHeaderParameter({parameters.Usage}[{i}], {{0}});", Use.Type<IMartenSession>());
        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"writer.Write({typeof(DBNull).FullNameInCode()}.Value, {{0}});", DbType);
        }

        public override void GenerateBulkWriterCodeAsync(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.CodeAsync($"await writer.WriteAsync({typeof(DBNull).FullNameInCode()}.Value, {{0}}, {{1}});", DbType, Use.Type<CancellationToken>());
        }
    }
}
