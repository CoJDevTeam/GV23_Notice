namespace GV23_Notice.Services.ClaThirdPartyApplications
{
    public interface IClaThirdPartyDateConfigurationService
    {
        ClaThirdPartyDateConfigurationResult Calculate(
            DateTime letterDate);
    }

    public sealed class ClaThirdPartyDateConfigurationService
        : IClaThirdPartyDateConfigurationService
    {
        public ClaThirdPartyDateConfigurationResult Calculate(
            DateTime letterDate)
        {
            var startDate = letterDate.Date;

            return new ClaThirdPartyDateConfigurationResult
            {
                LetterDate = startDate,
                RepresentationStartDate = startDate,
                RepresentationCloseDate =
                    startDate.AddDays(30)
            };
        }
    }

    public sealed class ClaThirdPartyDateConfigurationResult
    {
        public DateTime LetterDate { get; set; }

        public DateTime RepresentationStartDate { get; set; }

        public DateTime RepresentationCloseDate { get; set; }
    }
}