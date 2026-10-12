Public Module SampleModule
  Private _callCount As Integer = 0

  Public ReadOnly Property CallCount As Integer
    Get
      Return _callCount
    End Get
  End Property

  Public Function BuildGreeting(name As String, repeatCount As Integer) As String
    _callCount += 1

    If String.IsNullOrWhiteSpace(name) Then
      name = "Guest"
    End If

    Dim message As String = String.Empty
    For index As Integer = 1 To repeatCount
      message &= $"Hello, {name}! "
    Next

    Return message.Trim()
  End Function
End Module
