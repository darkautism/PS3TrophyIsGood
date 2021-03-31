# PS3TrophyIsGood

Best ps3 trophy editor ever

![DEMO](http://4.bp.blogspot.com/-dMj1nom1pKc/USnCAcmDu6I/AAAAAAAADWg/UFiD6o3uguU/s1600/t1.png)

## Build Tutorial

	git clone https://github.com/darkautism/PS3TrophyIsGood.git
	git submodule init
	git submodule update --recursive

After that, you can use visual studio to compile this project.

## Download

[CI auto build](https://github.com/darkautism/PS3TrophyIsGood/actions)

![Download link](https://user-images.githubusercontent.com/3898040/108462066-e8c87e00-72b6-11eb-80e1-1447c9cc2c2a.png)

## 警告

- 同步修改獎杯至PSN是危險的，有被ban的可能
- 獎杯損壞可能
- 同步失敗可能

即使如此也要修改，總而言之，風險自負。

請先確定以閱讀以上文字後再按下繼續閱讀。


## Change Log：

	v1.3.7
	1.  I've added a Resign option, so that you can resign a trophy folder to your specified PARAM.SFO (containing the desired account ID.)
	    - To use this, you must place your own param.sfo in the directory on your computer, "profiles" with any name (as long as it keeps the extension). For example, you could use "DARKNACHO.SFO"
	    - Otherwise, if you do not decide to use the feature, the tool will be default to the account id of the provided trophy folder.

	2. You can copy timestamps of a user from https://psntrophyleaders.com/
	    - This lets you copy all the timestamps from a user's game.
	3. Smart copy option
	    - This allows you to add a few parameters to make the copy look more legitimate, and not just copy-and-paste of the user's profile.
	    - Allows you to add a specific time to bring it to make the times more recent.
	    - Also adds random delta variable to each trophy to make it different
	    - Note: This also detects trophies earned in the same interval, so it adds it the same delta.

	3. Detects DLC from games.
	4. You can unlock the platinum trophy without needing to timestamp any DLC trophies.
	5. Detects if the game has a platinum trophy or not.
	    - If the game does not have a platinum trophy, the user can unlock the first trophy without receiving an error message.

	V1.3.6
	-add random setting
	
	V1.2.6
	　-修正一個-8 TimeZone會錯誤的問題
	
	V1.2.5
	　-修正TimeZone問題(就是玩家們反映時間+8的問題)
	　-整理多國語言，現在可以從選單上面選取。
	　-更改gobal.conf設定
	
	V1.1.4
	　-add random Timestamp feature
	　-瞬間白金現在是隨機時間
	
	V1.0.3
	　-修正一個修改時間的BUG
	
	V1.0.2
	　-修正一個重大BUG
	
	V1.0.1
	　-新增軟體圖像。
	　-新增多國語言。
	　-瞬間白金功能會自動將白金排在最後面（時間會是其他獎杯+1秒）。
	　-去除不必要的Debug Code，開啟獎杯時更快。
	　-現在開始白金必須最後解鎖
	　（但是時間還是自己輸入，請將白金時間設在最後，不然仍會無法同步白金）

## Tutorial

P.S. 本程式已附pfdtool且會自行加解密，請勿自行解密。每次修改完後一定要關閉檔案或關閉程式，這樣獎杯才會重新加密

灰色表示未取得，紅色表是已同步至PSN的檔案

白色表示已經取得，但是尚未同步至PSN的檔案

![DEMO1](http://4.bp.blogspot.com/-dMj1nom1pKc/USnCAcmDu6I/AAAAAAAADWg/UFiD6o3uguU/s1600/t1.png)

綠圈為獎杯名稱，紅圈為取得率

藍色為PSN經驗值，代表這獎杯同步至PSN後你的PSN帳號可以獲得多少經驗

最底下是本部落格地址，你可以點擊來查看本部落格最新資訊。

![DEMO2](http://1.bp.blogspot.com/-wl5TyzveZ3A/USnDCY--SzI/AAAAAAAADW8/UVHzFwXgaTo/s1600/T2.png)

### 開啟檔案

如果覺得點選資料夾開啟很麻煩，你可以把獎盃資料夾直接拖移進視窗，它會自行讀取。

## 實際演練

如果你獎杯尚未同步至PSN過，請將console id、user id設定正確。要修改console id請修改gobal.conf

### Step 1

　　將獎杯從你的PS3複製出來，位置在 /dev_hdd0/home/000000XX/trophy/

### Step 2

　　開啟程式，並將剛剛複製的獎杯資料夾拖移至程式上

### Step 3

　　將改完的獎杯複製回去(請備份)

### Step 4

![outside](http://2.bp.blogspot.com/-v8NAzSPKSHo/USnB_kbSsbI/AAAAAAAADWM/KKRthffJW2g/s1600/TVCAM%25E8%25A3%259D%25E7%25BD%25AE_20130224_145637.289.jpg)

獎杯變2%了


但是實際去看還是鎖著的，我不知道為什麼會有這種現象，假如你將所有獎杯解鎖，他會顯示100%但是點進去仍是全鎖，不過這樣子我們仍可同步至PSN

![befor sync](http://4.bp.blogspot.com/-yLq0hQb8b28/USnCAKajXSI/AAAAAAAADWY/8ovaRs6eQZ0/s1600/TVCAM%25E8%25A3%259D%25E7%25BD%25AE_20130224_145652.047.jpg)

### Step 5

注意，此步驟非常危險，請確定你在做甚麼，且了解後果。被ban我沒辦法還你PSN帳號和consoleid (idps)

![AFTER SYNC](http://3.bp.blogspot.com/-69ay5OYMsYo/USnB_Xy0ngI/AAAAAAAADWQ/K5YI4SBrAiI/s1600/TVCAM%25E8%25A3%259D%25E7%25BD%25AE_20130224_145646.119.jpg)


同步後獎杯就出現了，在這一步我遇到了同步錯誤，但之後就顯示了獎杯。

在vita上面的樣子，真的同步至PSN了。

![VITA](http://3.bp.blogspot.com/-_Gn65OQVVX8/USnB_ZbHaeI/AAAAAAAADWI/xq-PS-BjwFk/s1600/2013-02-24-145001.jpg)
