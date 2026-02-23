	SELECT --TOP 50
			RIGHT( REPLICATE('0', 3) + CAST(SubSystemID as VARCHAR(3)), 3) + '\' +
			SUBSTRING(CAST(FileUID as CHAR(36)), 1, 2) + '\' +
			SUBSTRING(CAST(FileUID AS CHAR(36)), 3, 2) + '\' +
			REPLACE( CAST(FileUID AS CHAR(36)), '-', '') + '.jpg' 
		AS SourcePath,
		Username,
		CONVERT(VARCHAR, ItemDate, 23) AS ItemDate,
		ItemID,
		'.jpg' AS Extension,
		ItemName
	FROM Ark_Item 
	INNER JOIN Ark_User
		ON Ark_Item.OwnerAccountID = Ark_User.ActiveAccountID
	WHERE PathVersion = 1
	AND OwnerAccountID <> 1
UNION
	SELECT --TOP 50
			RIGHT( REPLICATE('0', 3) + CAST(SubSystemID as VARCHAR(3)), 3) + '\' +
			SUBSTRING(CAST(FileUID as CHAR(36)), 1, 2) + '\' +
			SUBSTRING(CAST(FileUID AS CHAR(36)), 3, 2) + '\' +
			REPLACE( CAST(FileUID AS CHAR(36)), '-', '') + '-' +
			RIGHT( REPLICATE('0', 10) + CAST(OwnerAccountID AS VARCHAR(10)), 10) + '-' +
			RIGHT( REPLICATE('0', 10) + CAST(ItemID AS VARCHAR(10)), 10) + '-' +
			RIGHT( REPLICATE('0', 5) + CAST(GREATEST(Width,Height) AS VARCHAR(5)), 5) + 'L'	+ '-' +
			REPLACE( CAST(PathSecret AS CHAR(36)), '-', '') +
			CASE MediaType 
				WHEN 'image/jpeg' THEN '.jpg'
				WHEN 'image/png' THEN '.png'
				WHEN 'image/gif' THEN '.gif'
				WHEN 'image/bmp' THEN '.bmp'
				WHEN 'image/tiff' THEN '.tif'
			END 
		AS SourcePath,
		Username,
		CONVERT(VARCHAR, ItemDate, 23) AS ItemDate,
		ItemID,
		CASE MediaType 
			WHEN 'image/jpeg' THEN '.jpg'
			WHEN 'image/png' THEN '.png'
			WHEN 'image/gif' THEN '.gif'
			WHEN 'image/bmp' THEN '.bmp'
			WHEN 'image/tiff' THEN '.tif'
		END 
		AS Extension,
		ItemName
	FROM Ark_Item
	INNER JOIN Ark_User
		ON Ark_Item.OwnerAccountID = Ark_User.ActiveAccountID
	WHERE PathVersion = 2
	AND OwnerAccountID <> 1
ORDER BY SourcePath
