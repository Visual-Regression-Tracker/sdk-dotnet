using System;

namespace VisualRegressionTracker
{
    public class VisualRegressionTrackerError : Exception
    {
        public VisualRegressionTrackerError(string message) : base(message) { }
        public VisualRegressionTrackerError(Exception exception) : base(exception.Message, exception) { }
        public VisualRegressionTrackerError(string message, Exception exception) : base(message, exception) { }
    }
}