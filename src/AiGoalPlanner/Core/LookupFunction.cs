using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One Lookup as the model sees it: a hand-written <see cref="AIFunction"/> with a literal JSON
    /// schema, per ADR-0008. Blish HUD embeds System.Text.Json 5.0 and loads it before any module,
    /// so the module always runs on 5.0; the serializer-driven paths
    /// (<c>AIFunctionFactory.Create</c>, <c>AIFunction.JsonSerializerOptions</c>,
    /// <c>AIJsonUtilities</c> defaults) need System.Text.Json 8 or later and crash on it. A Lookup
    /// therefore carries its schema as text parsed once, reads its arguments as plain values, and
    /// returns its result as the string the model reads.
    /// </summary>
    public abstract class LookupFunction : AIFunction
    {
        private readonly string       _name;
        private readonly string       _description;
        private readonly JsonDocument _schema;

        /// <summary>
        /// Builds the function. <paramref name="schema"/> is the tool's JSON schema as text; the
        /// caller keeps the parsed <see cref="JsonDocument"/> alive, usually once per process.
        /// </summary>
        protected LookupFunction(string name, string description, JsonDocument schema) {
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            _name        = name ?? throw new ArgumentNullException(nameof(name));
            _description = description ?? string.Empty;
            _schema      = schema;
        }

        public override string Name => _name;

        public override string Description => _description;

        /// <summary>
        /// The literal schema the Lookup was built with. <see cref="JsonElement"/> is used only
        /// through the APIs System.Text.Json 5.0 has, such as <see cref="JsonElement.GetRawText"/>.
        /// </summary>
        public override JsonElement JsonSchema => _schema.RootElement;

        /// <summary>
        /// Runs the Lookup with the arguments the model sent, as plain values: strings, numbers,
        /// booleans and null — no serializer options anywhere.
        /// </summary>
        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
            return new ValueTask<object>(InvokeLookupAsync(arguments, cancellationToken));
        }

        /// <summary>
        /// The Lookup itself: what the tool does with the arguments it was given.
        /// </summary>
        protected abstract Task<object> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken);
    }
}
