using System;

namespace OmenSuperHub {
  public sealed class OperationResult {
    public bool Succeeded { get; private set; }
    public string Message { get; private set; }
    public Exception Exception { get; private set; }

    private OperationResult(bool succeeded, string message, Exception exception) {
      Succeeded = succeeded;
      Message = message ?? string.Empty;
      Exception = exception;
    }

    public static OperationResult Success(string message = "") {
      return new OperationResult(true, message, null);
    }

    public static OperationResult Failure(string message, Exception exception = null) {
      return new OperationResult(false, message, exception);
    }
  }
}
