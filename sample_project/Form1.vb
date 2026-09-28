Public Class Form1
  Private number As Integer

  Public Sub TestFunction()
    Debug.Print("Call Test Function")
    Debug.Print(GlobalContext.Instance.BuildStatusMessage("Form1", New Integer() {10, 20, 30}))
  End Sub

  Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
    If True Then
      Debug.Print("Hello world")
    End If

    TestFunction()

  End Sub

  Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
    MessageBox.Show("Hello world")

    If number > 0 Then
      TestFunction()
    End If

    TestFlow(10)
    TestFlow(0)
  End Sub

  Public Function TestFlow(value As Integer) As Integer
    Dim threshold As Integer = 10
    Dim multiplier As Integer = 2
    Dim increment As Integer = 1
    Dim loopStart As Integer = 0
    Dim loopEnd As Integer = 2
    Dim normalizedValue As Integer = value
    Dim adjustment As Integer = 0
    Dim lowerLimit As Integer = 0
    Dim upperLimit As Integer = 100
    Dim adjustedValue As Integer = normalizedValue + adjustment
    value = Math.Min(Math.Max(adjustedValue, lowerLimit), upperLimit)

    If value > threshold Then
      value = value * multiplier
    Else
      value = value + increment
    End If

    For i As Integer = loopStart To loopEnd
      value += i
    Next

    Return value

  End Function
End Class
