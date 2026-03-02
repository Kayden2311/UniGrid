package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Member", schema = "dbo")
public class Member {
    @Id
    @Column(name = "memberId", nullable = false)
    private UUID id;

    @Nationalized
    @Column(name = "email", length = 100)
    private String email;

    @Nationalized
    @Column(name = "password", nullable = false)
    private String password;

    @Nationalized
    @Column(name = "avatarURL")
    private String avatarURL;

    @Nationalized
    @Column(name = "status", nullable = false, length = 30)
    private String status;

    @Nationalized
    @Column(name = "userName", nullable = false, length = 50)
    private String userName;

    @Nationalized
    @Column(name = "phoneNumber", length = 20)
    private String phoneNumber;

    @Nationalized
    @Column(name = "fullName", length = 100)
    private String fullName;

    @ColumnDefault("0")
    @Column(name = "tier")
    private Integer tier;


}