using Riok.Mapperly.Abstractions;
using TalentSuite.Server.Bids.Data.Models;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared.Bids.List;

namespace TalentSuite.Server.Bids.Mappers;

[Mapper]
public partial class BidMapper
{
    public partial ParsedDocumentResponse ToResponse(ParsedDocumentModel source);
    public partial ParsedQuestionResponse ToResponse(ParsedQuestionModel source);
    public partial BidResponse ToResponse(BidModel source);
    public partial BidLibraryPushResponse ToResponse(BidLibraryPushModel source);
    public partial PagedBidListResponse ToResponse(PagedBidListModel source);
    public partial BidListItemResponse ToResponse(BidListItemModel source);
    public partial QuestionResponse ToResponse(CreateQuestionModel source);
    public partial DraftResponse ToResponse(DraftModel source);
    public partial DraftCommentResponse ToResponse(DraftCommentModel source);
    public partial DraftCommentResponse ToResponse(DraftCommentDataModel source);
    public partial CreateBidModel ToModel(CreateBidRequest source);
    public partial CreateQuestionModel ToModel(CreateQuestionRequest source);
    public partial PagedBidListModel ToModel(SearchDataModel source);
    public partial BidListItemModel ToModel(SearchItemDataModel source);
    public partial DraftModel ToModel(DraftDataModel source);
    public partial DraftCommentModel ToModel(DraftCommentDataModel source);
    public partial CreateQuestionModel ToModel(QuestionDataModel source);
    public partial List<DraftResponse> ToDraftResponses(List<DraftModel> source);
    public partial List<DraftCommentResponse> ToDraftCommentResponses(List<DraftCommentModel> source);
    public partial List<DraftModel> ToDraftModels(List<DraftDataModel> source);
    public partial List<DraftCommentModel> ToDraftCommentModels(List<DraftCommentDataModel> source);

    public BidDataModel ToDataModel(CreateBidModel source)
    {
        var target = new BidDataModel(Guid.NewGuid().ToString())
        {
            UniqueReference = source.UniqueReference,
            Company = source.Company,
            Summary = source.Summary,
            KeyInformation = source.KeyInformation,
            Budget = source.Budget,
            DeadlineForQualifying = source.DeadlineForQualifying,
            DeadlineForSubmission = source.DeadlineForSubmission,
            LengthOfContract = source.LengthOfContract,
            Stage = source.Stage,
            Status = source.Status,
            Questions = source.Questions.Select(ToDataModelWithNewId).ToList()
        };

        return target;
    }

    public BidModel ToModel(BidDataModel source)
    {
        var model = ToBidModelCore(source);
        model.Id = Guid.TryParse(source.Id, out var id) ? id : Guid.Empty;
        model.BidLibraryPush = source.BidLibraryPush is null
            ? null
            : new BidLibraryPushModel
            {
                BidId = source.BidLibraryPush.BidId,
                PerformedByUserId = source.BidLibraryPush.PerformedByUserId,
                PerformedByName = source.BidLibraryPush.PerformedByName,
                PushedAtUtc = source.BidLibraryPush.PushedAtUtc
            };
        return model;
    }

    public SearchItemDataModel ToDataModel(BidDataModel source)
    {
        return new SearchItemDataModel
        {
            Id = source.Id,
            Company = source.Company ?? string.Empty,
            Summary = source.Summary ?? string.Empty,
            QuestionCount = source.Questions?.Count ?? 0,
            Status = source.Status
        };
    }

    private static QuestionDataModel ToDataModelWithNewId(CreateQuestionModel source)
    {
        return new QuestionDataModel(Guid.NewGuid().ToString())
        {
            QuestionOrderIndex = source.QuestionOrderIndex,
            Number = source.Number,
            Title = source.Title,
            Category = source.Category,
            Description = source.Description,
            Length = source.Length,
            Weighting = source.Weighting,
            Required = source.Required,
            NiceToHave = source.NiceToHave
        };
    }

    private partial BidModel ToBidModelCore(BidDataModel source);
}
