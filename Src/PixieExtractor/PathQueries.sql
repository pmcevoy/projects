SELECT TOP 100 
	'/' +
	RIGHT( REPLICATE('0', 3) + CAST(FileSystemID as VARCHAR(3)), 3)
	+ '/' +
	RIGHT( REPLICATE('0', 3) + CAST(SubSystemID as VARCHAR(3)), 3)
	+ '/' +
	SUBSTRING(CAST(FileUID as CHAR(36)), 1, 2)
	+ '/' +
	SUBSTRING(CAST(FileUID AS CHAR(36)), 3, 2)
	+ '/' +
	CAST(FileUID AS CHAR(36)) 
+ '.jpg' AS SourcePath,
	'/' +
	Username
	+ '/' + 
	UploadBatchID 
	+ '/' + 
	ItemName
	+ '.jpg'  AS TargetPath
FROM Ark_Item 
INNER JOIN Ark_User
	ON Ark_Item.OwnerAccountID = Ark_User.ActiveAccountID
WHERE PathVersion = 1


SELECT TOP 100 
	'/' +
	RIGHT( REPLICATE('0', 3) + CAST(FileSystemID as VARCHAR(3)), 3)
	+ '/' +
	RIGHT( REPLICATE('0', 3) + CAST(SubSystemID as VARCHAR(3)), 3)
	+ '/' +
	SUBSTRING(CAST(FileUID as CHAR(36)), 1, 2)
	+ '/' +
	SUBSTRING(CAST(FileUID AS CHAR(36)), 3, 2)
	+ '/' +
	CAST(FileUID AS CHAR(36)) 
+ '.jpg' AS SourcePath,
	'/' +
	Username
	+ '/' + 
	UploadBatchID 
	+ '/' + 
	ItemName
	+ '.jpg'  AS TargetPath
FROM Ark_Item 
INNER JOIN Ark_User
	ON Ark_Item.OwnerAccountID = Ark_User.ActiveAccountID
WHERE PathVersion = 2


--SELECT DISTINCT Rotation
--FROM Ark_Item
--GROUP BY ItemName
----HAVING COUNT(*) > 1

--SELECT Username, COUNT(*)
--FROM Ark_User
--GROUP BY UserName
--HAVING COUNT(*) > 1
