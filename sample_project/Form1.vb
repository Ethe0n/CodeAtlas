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

    If value > 10 Then
      value = value * 2
    Else
      value = value + 1
    End If

    For i As Integer = 0 To 2
      value += i
    Next

    Return value

  End Function
End Class
