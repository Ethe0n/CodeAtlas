Public Class Form1
  Private number As Integer

  Public Sub TestFunction()
    Debug.Print("Call Test Function")
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

    Dim index As Integer = 0
    While index < 2
      value = value + index
      index = index + 1
    End While

    Return value
  End Function
End Class
