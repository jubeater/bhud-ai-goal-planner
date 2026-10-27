using System;
using System.Diagnostics;
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
    /// <remarks>
    /// Every Lookup is timed and logged here, once, so a slow or odd Request can be diagnosed from
    /// the Blish HUD log and no Lookup added later has to remember to do it. The line holds the
    /// Lookup's name, its duration, what it read and how big its answer is; what it read comes from
    /// <see cref="LookupAnswer.LogDetail"/>, which carries names and counts, never a Setting's value.
    /// </remarks>
    public abstract class LookupFunction : AIFunction
    {
        private readonly string         _name;
        private readonly string         _description;
        private readonly JsonDocument   _schema;
        private readonly Action<string> _logDebug;

        /// <summary>
        /// Builds the function. <paramref name="schema"/> is the tool's JSON schema as text; the
        /// caller keeps the parsed <see cref="JsonDocument"/> alive, usually once per process.
        /// <paramref name="logDebug"/> receives one line per call; without it the Lookup logs
        /// nothing.
        /// </summary>
        protected LookupFunction(string name, string description, JsonDocument schema, Action<string> logDebug = null) {
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            _name        = name ?? throw new ArgumentNullException(nameof(name));
            _description = description ?? string.Empty;
            _schema      = schema;
            _logDebug    = logDebug;
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
        /// booleans and null — no serializer options anywhere. A Lookup that fails or is cancelled
        /// is logged as such before the failure reaches the Chat.
        /// </summary>
        protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
            Stopwatch clock = Stopwatch.StartNew();

            try {
                LookupAnswer answer = await InvokeLookupAsync(arguments, cancellationToken).ConfigureAwait(false);

                Log(clock, DescribeForLog(answer));

                return answer.Text;
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                Log(clock, "cancelled");

                throw;
            } catch (Exception exception) {
                // The type, not the message: a message can carry a Setting's value back out.
                Log(clock, "failed: " + exception.GetType().Name);

                throw;
            }
        }

        /// <summary>
        /// The Lookup itself: what the tool does with the arguments it was given.
        /// </summary>
        protected abstract Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken);

        private static string DescribeForLog(LookupAnswer answer) {
            string what = string.IsNullOrEmpty(answer.LogDetail) ? "ok" : answer.LogDetail;

            return what + ", " + answer.Text.Length + " chars";
        }

        private void Log(Stopwatch clock, string what) {
            if (_logDebug == null) return;

            _logDebug("Lookup " + _name + ": " + clock.ElapsedMilliseconds + " ms, " + what);
        }
    }
}
