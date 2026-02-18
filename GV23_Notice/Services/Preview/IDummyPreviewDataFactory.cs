namespace GV23_Notice.Services.Preview
{
    public interface IDummyPreviewDataFactory
    {
        (string objectionNo, string appealNo) GetSampleRefs(string rollShortCode);
        DummyRecipient GetRecipient();
        DummyProperty GetProperty();
    }

    public sealed record DummyRecipient(string Name, string Email, string AddressLine);
    public sealed record DummyProperty(string PropertyDescription, string PremiseId, string Category, decimal MarketValue, decimal Extent, DateOnly EffectiveDate);

}
