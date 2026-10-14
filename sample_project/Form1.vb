Imports System.Security.AccessControl

Public Class Form1
  Private number As Integer

  Public Sub TestFunction()
    Debug.Print("Call Test Function")
    Debug.Print(GlobalContext.Instance.BuildStatusMessage("Form1", New Integer() {10, 20, 30}))
  End Sub

  Public Sub EmptyStringTest()
    Dim test_string = ""
  End Sub

  Public Sub LongConditionTest()
    Dim test_string = "hello"

    If test_string = "hello" Or test_string = "world" Or test_string = "test" Or test_string = "world2" Then
      Debug.Print("hello world")
    End If

  End Sub

  Public Sub LongParameterListTest(a As Integer, b As String, c As Integer, long_parameter_name_like_this As String, long_parameter_name_like_this_again As Integer)

  End Sub

  Public Sub TryCatchTest()
    Try
      Debug.Print("try test")
      Debug.Print("try test 2")
      Debug.Print("try test 3")

      For index As Integer = 1 To 5
        Debug.Write(index.ToString & " ")
      Next

    Catch ex As Exception
      Debug.Print("Catch")

    Finally
      Debug.Print("Finally")
    End Try

    Debug.Print("Try - Catch End")

  End Sub

  Public Sub UsingTest()
    Using writer As System.IO.TextWriter = System.IO.File.CreateText("log.txt")
      writer.WriteLine("This is line one.")
      writer.WriteLine("This is line two.")
    End Using
  End Sub

  Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
    If True Then
      Debug.Print("Hello world")
    End If

    TestFunction()
    LongParameterListTest(1, "2", 3, "", 5)

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
    Dim colonStep1 As Integer = value : Dim colonStep2 As Integer = colonStep1 + 1 : Dim colonStep3 As Integer = colonStep2 + 1 : value = colonStep3 - 2

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

  Public Sub TestLegacyErrorFlow()
    On Error Resume Next
    Dim legacyValue As Integer = 1
    Debug.Print(legacyValue)
  End Sub

  Public Function TestSelectCase(value As Integer) As String
    Dim result As String

    Select Case value
      Case 1
        result = "One"
        If True Then
          Debug.Print("hello")
        ElseIf False Then
          Debug.Print("hello")
        Else
          Debug.Print("hello")
        End If
      Case 2, 3
        result = "Two or Three"
        If True Then
          Debug.Print("hello")
        ElseIf False Then
          Debug.Print("hello")
        Else
          Debug.Print("hello")
        End If
      Case 4 To 6
        result = "Range"
      Case Is >= 10
        result = "High"
        If True Then
          Debug.Print("hello")
        ElseIf False Then
          Debug.Print("hello")
        Else
          Debug.Print("hello")
        End If
      Case Else
        If True Then
          Debug.Print("hello")
        ElseIf False Then
          Debug.Print("hello")
        Else
          Debug.Print("hello")
        End If
        result = "Other"
        Debug.Print("case not found")
    End Select

    Return result
  End Function
End Class
