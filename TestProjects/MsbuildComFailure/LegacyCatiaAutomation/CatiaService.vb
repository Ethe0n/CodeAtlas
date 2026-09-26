Namespace LegacyCatiaAutomation
    Public Class CatiaService
        Private ReadOnly _application As INFITF.Application

        Public Sub New(application As INFITF.Application)
            _application = application
        End Sub

        Public Function UpdateActivePart() As Boolean
            Dim document As INFITF.Document = _application.ActiveDocument
            Dim partDocument As MECMOD.PartDocument = TryCast(document, MECMOD.PartDocument)

            If partDocument Is Nothing Then
                Return False
            End If

            partDocument.Part.Update()
            document.Save()
            Return True
        End Function
    End Class
End Namespace
