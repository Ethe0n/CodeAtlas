Public NotInheritable Class GlobalContext
  Public Class UserScoreSummary
    Public Property UserName As String = String.Empty
    Public Property TotalScore As Integer
  End Class

  Private Shared ReadOnly _instance As New GlobalContext()

  Private _count As Integer = 0
  Private _requestCount As Integer
  Private _lastMessage As String = String.Empty

  Private Sub New()
  End Sub

  Public Shared ReadOnly Property Instance As GlobalContext
    Get
      Return _instance
    End Get
  End Property

  Public Property IsDebugMode As Boolean

  Public ReadOnly Property RequestCount As Integer
    Get
      Return _requestCount
    End Get
  End Property

  Public ReadOnly Property LastMessage As String
    Get
      Return _lastMessage
    End Get
  End Property

  Public Sub TestFieldUsage()
    _count = 10
    Debug.Print(_count)
    _count += 1
  End Sub

  Public Function BuildStatusMessage(userName As String, scores As Integer()) As String
    _requestCount += 1

    If String.IsNullOrWhiteSpace(userName) Then
      _lastMessage = "Unknown user"
      Return _lastMessage
    End If

    Dim total As Integer = 0
    For Each score As Integer In scores
      If score < 0 Then
        Continue For
      End If

      total += score
    Next

    If total > 100 Then
      _lastMessage = $"{userName}: High"
    ElseIf total > 50 Then
      _lastMessage = $"{userName}: Medium"
    Else
      _lastMessage = $"{userName}: Low"
    End If

    Return _lastMessage
  End Function

  Public Function CountMatchingCells(matrix As Integer(,), threshold As Integer) As Integer
    Dim matches As Integer = 0

    For row As Integer = 0 To matrix.GetLength(0) - 1
      For column As Integer = 0 To matrix.GetLength(1) - 1
        If matrix(row, column) >= threshold Then
          matches += 1
        End If
      Next
    Next

    Return matches
  End Function
End Class

Public Class DependencySampleA
  Private _b As DependencySampleB

  Public Property CurrentB As DependencySampleB

  Public Function CreateB(value As DependencySampleB) As DependencySampleB
    Dim result = New DependencySampleB()
    result.DoSomething()
    Return result
  End Function
End Class

Public Class DependencySampleB
  Public Sub DoSomething()
  End Sub
End Class
