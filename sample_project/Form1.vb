Public Class Form1
  Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
    If True Then
      Debug.Print("Hello world")
    End If

  End Sub

  Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
    MessageBox.Show("Hello world")
  End Sub
End Class
