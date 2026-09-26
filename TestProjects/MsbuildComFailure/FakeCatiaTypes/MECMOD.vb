Namespace MECMOD
    Public Interface PartDocument
        Inherits INFITF.Document
        ReadOnly Property Part As Part
    End Interface

    Public Interface Part
        Sub Update()
    End Interface
End Namespace
