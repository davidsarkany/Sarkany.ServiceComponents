using ServiceComponents.Application.Monitoring;

namespace ReferenceApplication.Application.TestProcess
{
    [SummaryMetric("business_process_summary", "Business processes")]
    public class BusinessProcessMetric
    {
        public string ProcessName { get; }

        public BusinessProcessMetric(string processName)
        {
            ProcessName = processName;
        }
    }
}