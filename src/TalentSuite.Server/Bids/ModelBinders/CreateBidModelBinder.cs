using Microsoft.AspNetCore.Mvc.ModelBinding;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.ModelBinders;

public sealed class CreateBidModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext is null)
            throw new ArgumentNullException(nameof(bindingContext));

        var req = bindingContext.HttpContext.Request;

        if (!req.HasFormContentType)
        {
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        var form = req.Form;

        var model = new CreateBidRequest
        {
            Company = form["Company"],
            Summary = form["Summary"],
            KeyInformation = form["KeyInformation"],
            Budget = form["Budget"],
            DeadlineForQualifying = form["DeadlineForQualifying"],
            DeadlineForSubmission = form["DeadlineForSubmission"],
            LengthOfContract = form["LengthOfContract"],
            Stage = ParseStage(form["Stage"]),
            Questions = new List<CreateQuestionRequest>()
        };

        var indexes = GetIndexes(form, "Questions");

        foreach (var i in indexes)
        {
            
            var q = new CreateQuestionRequest
            {
                QuestionOrderIndex = ParseInt(form[$"Questions[{i}].QuestionOrderIndex"]),
                Number = form[$"Questions[{i}].Number"],
                Category = form[$"Questions[{i}].Category"],
                Title = form[$"Questions[{i}].Title"],
                Description = form[$"Questions[{i}].Description"],
                Length = form[$"Questions[{i}].Length"],
                Weighting = ParseInt(form[$"Questions[{i}].Weighting"]),
                Required = ParseBool(form[$"Questions[{i}].Required"]),
                NiceToHave = ParseBool(form[$"Questions[{i}].NiceToHave"])
            };

            model.Questions.Add(q);
        }

        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }

    private static IEnumerable<int> GetIndexes(IFormCollection form, string prefix)
    {
        var indexes = new HashSet<int>();

        foreach (var key in form.Keys)
        {
            // match: Questions[123].Title
            if (!key.StartsWith(prefix + "[", StringComparison.OrdinalIgnoreCase))
                continue;

            var open = key.IndexOf('[', prefix.Length);
            var close = key.IndexOf(']', open + 1);
            if (open < 0 || close < 0) continue;

            var inside = key.Substring(open + 1, close - open - 1);
            if (int.TryParse(inside, out var idx))
                indexes.Add(idx);
        }

        return indexes.OrderBy(i => i);
    }

    private static int ParseInt(string? value)
        => int.TryParse(value, out var result) ? result : 0;

    private static bool ParseBool(string? value)
        => bool.TryParse(value, out var result) && result;

    private static BidStage ParseStage(string? value)
        => Enum.TryParse<BidStage>(value, true, out var result)
            ? result
            : BidStage.Stage1;
}
