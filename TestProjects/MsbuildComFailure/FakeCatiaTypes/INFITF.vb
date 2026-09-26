Namespace INFITF
    Public Interface Application
        ReadOnly Property ActiveDocument As Document
    End Interface

    Public Interface Document
        ReadOnly Property Name As String
        Sub Save()
        Sub Close()
    End Interface
End Namespace
